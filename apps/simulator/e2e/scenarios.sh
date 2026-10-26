#!/usr/bin/env bash
# Each scenario plays one match on server $1 and checks what the api made of
# it. The `sim:` word in a lineup's name is what tells the simulator what to
# play (see src/Scenario.cs).

# shellcheck disable=SC2034
SCENARIOS=(
    "bo3-2-0 series 3 2 0"
    "bo3-2-1 series 3 2 1"
    "bo3-1-2 series 3 1 2"
    "bo5-3-0 series 5 3 0"
    "bo5-3-1 series 5 3 1"
    "bo5-3-2 series 5 3 2"
    "tie tie"
    "surrender surrender"
    "noshow noshow"
    "noshow-tournament noshow_tournament"
    "disconnect disconnect"
    "abandon abandon"
)

ENDED="('Finished', 'Tie', 'Canceled', 'Forfeit', 'Surrendered')"

# Over once the api has ended the match, rated it and given the server back.
settled() {
    local match=$1 server=$2
    echo "SELECT m.status IN $ENDED
             AND EXISTS (SELECT 1 FROM player_elo pe WHERE pe.match_id = m.id)
             AND NOT EXISTS (SELECT 1 FROM servers s WHERE s.id = '$(server_id "$server")' AND s.reserved_by_match_id = m.id)
          FROM matches m WHERE m.id = '$match'"
}

# Over once the api has ended the match and given the server back, rated or not.
ended() {
    local match=$1 server=$2
    echo "SELECT m.status IN $ENDED
             AND NOT EXISTS (SELECT 1 FROM servers s WHERE s.id = '$(server_id "$server")' AND s.reserved_by_match_id = m.id)
          FROM matches m WHERE m.id = '$match'"
}

map_reaches() {
    echo "SELECT EXISTS (SELECT 1 FROM match_maps WHERE match_id = '$1' AND status = '$2')"
}

# A match the api leaves running would hold its server for the next scenario.
release() {
    local match=$1
    if [ "$(sql -c "SELECT status IN $ENDED FROM matches WHERE id = '$match'")" != "t" ]; then
        echo "  (the api left the match $(sql -c "SELECT status FROM matches WHERE id = '$match'"); canceling it to free the server)"
        sql -c "UPDATE matches SET status = 'Canceled' WHERE id = '$match'" >/dev/null
    fi
}

# series <server> <best of> <wins> <losses>: the second lineup takes the
# series wins-losses, the veto deciding the maps and sides.
series() {
    local server=$1 best_of=$2 wins=$3 losses=$4 match
    match=$(new_id)
    create_match "$match" "$server" "$best_of" "Alpha" "Bravo sim:$wins-$losses"
    start_match "$match"
    wait_for 900 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    local failed=0
    check -v match="$match" -v server="$(server_id "$server")" -v best_of="$best_of" \
        -v wins="$wins" -v losses="$losses" -v plan="$(series_plan "$wins" "$losses")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
subject AS (SELECT lineup_2_id AS id FROM m),
other AS (SELECT lineup_1_id AS id FROM m),
maps AS (
    SELECT mm.*, lineup_1_score(mm) AS l1, lineup_2_score(mm) AS l2
    FROM match_maps mm WHERE mm.match_id = :'match'
),
played AS (SELECT * FROM maps WHERE status = 'Finished'),
lineups AS (
    SELECT mlp.steam_id, mlp.match_lineup_id
    FROM match_lineup_players mlp, m
    WHERE mlp.match_lineup_id IN (m.lineup_1_id, m.lineup_2_id)
)
SELECT 'match status', 'Finished', (SELECT status FROM m)
UNION ALL
SELECT 'match winner',
       (SELECT CASE WHEN :wins > :losses THEN subject.id ELSE other.id END FROM subject, other)::text,
       (SELECT winning_lineup_id::text FROM m)
UNION ALL
SELECT 'veto made every map', :'best_of', (SELECT count(*)::text FROM maps)
UNION ALL
SELECT 'maps played', (:wins + :losses)::text, (SELECT count(*)::text FROM played)
UNION ALL
SELECT 'maps past the result never started', (:best_of - :wins - :losses)::text,
       (SELECT count(*)::text FROM maps
         WHERE status <> 'Finished'
           AND NOT EXISTS (SELECT 1 FROM match_map_rounds r WHERE r.match_map_id = maps.id))
UNION ALL
SELECT 'every map has a side each', '0',
       (SELECT count(*)::text FROM maps WHERE lineup_1_side IS NULL OR lineup_1_side = lineup_2_side)
UNION ALL
(SELECT 'map ' || p."order" || ' winner',
        (CASE substr(:'plan', p."order", 1) WHEN 'W' THEN (SELECT id FROM subject) ELSE (SELECT id FROM other) END)::text,
        p.winning_lineup_id::text
   FROM played p ORDER BY p."order")
UNION ALL
(SELECT 'map ' || p."order" || ' score', '13 to under 13',
        CASE WHEN greatest(p.l1, p.l2) = 13 AND least(p.l1, p.l2) < 13 THEN '13 to under 13' ELSE p.l1 || '-' || p.l2 END
   FROM played p ORDER BY p."order")
UNION ALL
(SELECT 'map ' || p."order" || ' every round recorded', (p.l1 + p.l2)::text,
        (SELECT count(DISTINCT r.round)::text FROM match_map_rounds r
          WHERE r.match_map_id = p.id AND r.deleted_at IS NULL AND r.round BETWEEN 1 AND p.l1 + p.l2)
   FROM played p ORDER BY p."order")
UNION ALL
(SELECT 'map ' || p."order" || ' kills recorded', 'true',
        (SELECT (count(*) > 0)::text FROM player_kills k WHERE k.match_map_id = p.id)
   FROM played p ORDER BY p."order")
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'series multiplier', abs(:wins - :losses)::text,
       (SELECT string_agg(DISTINCT series_multiplier::text, ',') FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'winners gain and losers lose', '10',
       (SELECT count(*)::text FROM player_elo pe JOIN lineups l ON l.steam_id = pe.steam_id
         WHERE pe.match_id = :'match'
           AND (l.match_lineup_id = (SELECT winning_lineup_id FROM m)) = (pe.change > 0))
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# tie <server>: a bo1 without overtime whose map ends 12-12.
tie() {
    local server=$1 match
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:tie" overtime=false live_match_timeout=2
    start_match "$match"
    wait_for 600 "$(map_reaches "$match" Finished)" || echo "  (timed out waiting for the map to finish)"
    # the live-match timeout is the api's backstop for a match nothing else ends
    wait_for 240 "$(ended "$match" "$server")" || true

    local failed=0
    check -v match="$match" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
maps AS (SELECT mm.*, lineup_1_score(mm) AS l1, lineup_2_score(mm) AS l2 FROM match_maps mm WHERE mm.match_id = :'match')
SELECT 'map status', 'Finished', (SELECT status FROM maps)
UNION ALL
SELECT 'map score', '12-12', (SELECT l1 || '-' || l2 FROM maps)
UNION ALL
SELECT 'every round recorded', '24',
       (SELECT count(DISTINCT r.round)::text FROM match_map_rounds r JOIN maps ON r.match_map_id = maps.id WHERE r.deleted_at IS NULL)
UNION ALL
SELECT 'match ends as a tie', 'Tie', (SELECT status FROM m)
UNION ALL
SELECT 'match has no winner', '', (SELECT coalesce(winning_lineup_id::text, '') FROM m)
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# surrender <server>: the second lineup votes to surrender after eight rounds
# and leaves; CS2 then hands every round to the first until the game ends.
surrender() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:surrender" live_match_timeout=3
    start_match "$match"
    wait_for 600 "$(map_reaches "$match" Surrendered)" || echo "  (timed out waiting for the surrender)"
    wait_for 300 "$(ended "$match" "$server")" || true

    check -v match="$match" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
map AS (SELECT * FROM match_maps WHERE match_id = :'match')
SELECT 'map status', 'Surrendered', (SELECT status FROM map)
UNION ALL
SELECT 'map goes to the lineup that stayed', (SELECT lineup_1_id::text FROM m), (SELECT winning_lineup_id::text FROM map)
UNION ALL
SELECT 'the lineup that gave up left', '5',
       (SELECT count(*)::text FROM match_lineup_players WHERE match_lineup_id = (SELECT lineup_2_id FROM m) AND NOT is_connected)
UNION ALL
SELECT 'match ends surrendered', 'Surrendered', (SELECT status FROM m)
UNION ALL
SELECT 'match goes to the lineup that stayed', (SELECT lineup_1_id::text FROM m), (SELECT coalesce(winning_lineup_id::text, '') FROM m)
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# The second lineup never connects, and the no-show deadline runs out.
no_show_checks() {
    check -v match="$1" -v server="$(server_id "$2")" -v status="$3" -v rated="$4" <<'SQL'
WITH m AS (SELECT * FROM matches WHERE id = :'match')
SELECT 'match status', :'status', (SELECT status FROM m)
UNION ALL
SELECT 'nothing was played', '0',
       (SELECT count(*)::text FROM match_map_rounds r JOIN match_maps mm ON mm.id = r.match_map_id WHERE mm.match_id = :'match')
UNION ALL
SELECT 'the lineup that never came is recorded as no-shows', '5',
       (SELECT count(*)::text FROM abandoned_matches am JOIN match_lineup_players mlp ON mlp.steam_id = am.steam_id
         WHERE am.match_id = :'match' AND mlp.match_lineup_id = (SELECT lineup_2_id FROM m))
UNION ALL
SELECT 'the lineup that turned up is not', '0',
       (SELECT count(*)::text FROM abandoned_matches am JOIN match_lineup_players mlp ON mlp.steam_id = am.steam_id
         WHERE am.match_id = :'match' AND mlp.match_lineup_id = (SELECT lineup_1_id FROM m))
UNION ALL
SELECT 'ratings', :'rated',
       (SELECT coalesce(string_agg(DISTINCT pe.change::text, ','), 'none') || ' x' || count(*)::text
          FROM player_elo pe WHERE pe.match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
}

# noshow <server>: a regular match is canceled and only the no-shows carry it.
noshow() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:noshow" auto_cancel_duration=1
    start_match "$match"
    wait_for 300 "$(ended "$match" "$server")" || echo "  (timed out waiting for the deadline)"
    no_show_checks "$match" "$server" Canceled "none x0" || failed=1
    release "$match"
    return "$failed"
}

# noshow_tournament <server>: in a bracket the lineup that turned up wins by
# forfeit, and the no-shows take the leaver penalty.
noshow_tournament() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:noshow" auto_cancel_duration=1
    make_tournament_match "$match"
    start_match "$match"
    wait_for 300 "$(settled "$match" "$server")" || echo "  (timed out waiting for the deadline)"
    no_show_checks "$match" "$server" Forfeit "-150 x5" || failed=1
    check -v match="$match" <<'SQL' || failed=1
SELECT 'forfeit goes to the lineup that turned up', lineup_1_id::text, coalesce(winning_lineup_id::text, '')
  FROM matches WHERE id = :'match'
SQL
    release "$match"
    return "$failed"
}

# disconnect <server>: a player drops after round five, the next round is
# paused until they reconnect, and the map plays out.
disconnect() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:disconnect"
    start_match "$match"
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    check -v match="$match" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match')
SELECT 'match status', 'Finished', (SELECT status FROM m)
UNION ALL
SELECT 'the pause raised an alert', 'true',
       (SELECT (count(*) > 0)::text FROM notifications WHERE entity_id = :'match' AND title = 'Match Alert: Map Paused')
UNION ALL
SELECT 'everyone is connected again', '10',
       (SELECT count(*)::text FROM match_lineup_players
         WHERE match_lineup_id IN ((SELECT lineup_1_id FROM m), (SELECT lineup_2_id FROM m)) AND is_connected)
UNION ALL
SELECT 'nobody is recorded as leaving', '0', (SELECT count(*)::text FROM abandoned_matches WHERE match_id = :'match')
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# abandon <server>: a player drops after round five and never returns; three
# minutes on they are reported and their team surrenders.
abandon() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:abandon" live_match_timeout=3
    start_match "$match"
    wait_for 600 "$(map_reaches "$match" Surrendered)" || echo "  (timed out waiting for the surrender)"
    wait_for 300 "$(ended "$match" "$server")" || true

    check -v match="$match" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
leaver AS (
    SELECT am.steam_id FROM abandoned_matches am
      JOIN match_lineup_players mlp ON mlp.steam_id = am.steam_id AND mlp.match_lineup_id = (SELECT lineup_2_id FROM m)
     WHERE am.match_id = :'match'
)
SELECT 'the leaver is recorded', '1', (SELECT count(*)::text FROM leaver)
UNION ALL
SELECT 'admins are told', 'true',
       (SELECT (count(*) > 0)::text FROM notifications WHERE entity_id = :'match' AND type = 'MatchAbandoned')
UNION ALL
SELECT 'map status', 'Surrendered', (SELECT status FROM match_maps WHERE match_id = :'match')
UNION ALL
SELECT 'match ends surrendered', 'Surrendered', (SELECT status FROM m)
UNION ALL
SELECT 'match goes to the lineup that stayed', (SELECT lineup_1_id::text FROM m), (SELECT coalesce(winning_lineup_id::text, '') FROM m)
UNION ALL
SELECT 'the leaver takes the leaver penalty', '-150',
       (SELECT coalesce(string_agg(pe.change::text, ','), 'unrated') FROM player_elo pe
         WHERE pe.match_id = :'match' AND pe.steam_id IN (SELECT steam_id FROM leaver))
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}
