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
    "resume resume"
    "restart restart"
    "timeouts timeouts"
    "chat chat"
    "coach coach"
    "camera camera"
    "demo demo"
    "ready-captains ready_3v3 Captains"
    "ready-admin ready_3v3 Admin"
    "rush rush"
    "rush-restart rush_restart"
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
    wait_for 300 "$(settled "$match" "$server")" || true

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

# Until the simulated server has dropped the match, as the api told it to.
sim_dropped() {
    local server=$1 match=$2 deadline=$((SECONDS + $3))
    until docker compose logs "sim-$server" 2>/dev/null | grep -q "clearing match $match"; do
        if [ "$SECONDS" -ge "$deadline" ]; then
            return 1
        fi
        sleep 2
    done
}

# How often the api answered a restore vote by putting the map back to a round.
restores_to() {
    docker compose logs api 2>/dev/null | grep -c "resetting match map $1 to round $2," || true
}

# Each map so far as order:status:winner:score.
maps_so_far() {
    echo "SELECT string_agg(\"order\" || ':' || status || ':' || coalesce(winning_lineup_id::text, '-')
                        || ':' || lineup_1_score(mm) || '-' || lineup_2_score(mm), ',' ORDER BY \"order\")
            FROM match_maps mm WHERE match_id = '$1' AND \"order\" <= $2"
}

# resume <server>: a bo5 canceled during its fourth map at 2-1, then resumed.
# The server it comes back on offers to restore the last round backed up
# before the cancel, and the series finishes 3-1 with the first three maps as
# they were.
resume() {
    local server=$1 match failed=0 map4 maps_before maps_after canceled_at restored logged
    match=$(new_id)
    create_match "$match" "$server" 5 "Alpha" "Bravo sim:3-1"
    start_match "$match"

    wait_for 900 "SELECT count(*) >= 5 FROM match_map_rounds r JOIN match_maps mm ON mm.id = r.match_map_id
                   WHERE mm.match_id = '$match' AND mm.\"order\" = 4 AND r.backup_file <> ''" \
        || echo "  (timed out waiting for the fourth map)"
    map4=$(sql -c "SELECT id FROM match_maps WHERE match_id = '$match' AND \"order\" = 4")
    maps_before=$(sql -c "$(maps_so_far "$match" 3)")

    sql -c "UPDATE matches SET status = 'Canceled' WHERE id = '$match'" >/dev/null
    wait_for 120 "SELECT server_id IS NULL AND NOT EXISTS (SELECT 1 FROM servers WHERE reserved_by_match_id = m.id)
                    FROM matches m WHERE id = '$match'" || echo "  (timed out waiting for the cancel)"
    sim_dropped "$server" "$match" 120 || echo "  (the server never dropped the match)"
    canceled_at=$(sql -c "SELECT now()")
    restored=$(sql -c "SELECT max(round) FROM match_map_rounds
                        WHERE match_map_id = '$map4' AND backup_file <> '' AND deleted_at IS NULL")

    # what resumeMatch does, with the server it had so the other queues keep theirs
    sql -c "UPDATE matches SET status = 'Live', server_id = '$(server_id "$server")'
             WHERE id = '$match' AND status = 'Canceled'" >/dev/null
    wait_for 900 "$(settled "$match" "$server")" || echo "  (timed out waiting for the series to finish)"

    logged=$(restores_to "$map4" "$restored")
    maps_after=$(sql -c "$(maps_so_far "$match" 3)")

    check -v match="$match" -v map4="$map4" -v maps_before="$maps_before" -v canceled_at="$canceled_at" \
        -v restored="$restored" -v logged="$logged" -v maps_after="$maps_after" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
alive AS (SELECT * FROM match_map_rounds WHERE match_map_id = :'map4' AND deleted_at IS NULL)
SELECT 'match status', 'Finished', (SELECT status FROM m)
UNION ALL
SELECT 'the series goes to the lineup that had it', (SELECT lineup_2_id::text FROM m), (SELECT coalesce(winning_lineup_id::text, '') FROM m)
UNION ALL
SELECT 'the first three maps are as they were', :'maps_before', :'maps_after'
UNION ALL
SELECT 'the vote restored the last round backed up before the cancel', '1', :'logged'
UNION ALL
SELECT 'the rounds up to it are the ones played before the cancel', :'restored',
       (SELECT count(*)::text FROM alive WHERE round <= :'restored'::int AND "time" < :'canceled_at'::timestamptz)
UNION ALL
SELECT 'no round of the fourth map is recorded twice', (SELECT count(*)::text FROM alive), (SELECT count(DISTINCT round)::text FROM alive)
UNION ALL
SELECT 'the fourth map goes to the lineup that had it', (SELECT lineup_2_id::text FROM m),
       (SELECT coalesce(winning_lineup_id::text, '') FROM match_maps WHERE id = :'map4')
UNION ALL
SELECT 'the fifth map is never played', 'Scheduled', (SELECT status FROM match_maps WHERE match_id = :'match' AND "order" = 5)
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'series multiplier', '2', (SELECT string_agg(DISTINCT series_multiplier::text, ',') FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true', (SELECT (NOT EXISTS (SELECT 1 FROM servers WHERE reserved_by_match_id = :'match'))::text)
SQL
    release "$match"
    return "$failed"
}

# restart <server>: the server stops mid-map and a new one comes up. It holds
# the match until everyone is back, the vote restores the last round backed
# up, and the map plays out from there.
restart() {
    local server=$1 match failed=0 map stopped_at restored logged
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:1-0"
    start_match "$match"

    wait_for 600 "SELECT count(*) >= 6 FROM match_map_rounds r JOIN match_maps mm ON mm.id = r.match_map_id
                   WHERE mm.match_id = '$match' AND r.backup_file <> ''" || echo "  (timed out waiting for the map)"
    docker compose kill "sim-$server" >/dev/null 2>&1
    stopped_at=$(sql -c "SELECT now()")
    map=$(sql -c "SELECT id FROM match_maps WHERE match_id = '$match'")
    restored=$(sql -c "SELECT max(round) FROM match_map_rounds
                        WHERE match_map_id = '$map' AND backup_file <> '' AND deleted_at IS NULL")
    docker compose up -d "sim-$server" >/dev/null 2>&1

    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the map to finish)"
    logged=$(restores_to "$map" "$restored")

    check -v match="$match" -v map="$map" -v stopped_at="$stopped_at" -v restored="$restored" \
        -v logged="$logged" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
alive AS (SELECT * FROM match_map_rounds WHERE match_map_id = :'map' AND deleted_at IS NULL)
SELECT 'match status', 'Finished', (SELECT status FROM m)
UNION ALL
SELECT 'the map goes to the lineup that had it', (SELECT lineup_2_id::text FROM m), (SELECT coalesce(winning_lineup_id::text, '') FROM m)
UNION ALL
SELECT 'the vote restored the last round backed up before the stop', '1', :'logged'
UNION ALL
SELECT 'the rounds up to it are the ones played before the stop', :'restored',
       (SELECT count(*)::text FROM alive WHERE round <= :'restored'::int AND "time" < :'stopped_at'::timestamptz)
UNION ALL
SELECT 'no round is recorded twice', (SELECT count(*)::text FROM alive), (SELECT count(DISTINCT round)::text FROM alive)
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# The api keeps its data in Redis db 1.
redis() {
    docker compose exec -T redis redis-cli -n 1 "$@"
}

# until <seconds> <command...>: until the command succeeds.
until_true() {
    local deadline=$((SECONDS + $1))
    shift
    until "$@" >/dev/null 2>&1; do
        if [ "$SECONDS" -ge "$deadline" ]; then
            return 1
        fi
        sleep 2
    done
}

# sim_logged <server> <text> [since]: since a time, for a container that has
# been restarted and still holds the lines of its last run.
sim_logged() {
    docker compose logs ${3:+--since "$3"} "sim-$1" 2>/dev/null | grep -q "$2"
}

# Read on every try: until_true runs its arguments again, not a $(...) in them.
in_lobby() {
    test "$(redis HEXISTS "chat:match:$1" "$2")" = 1
}

# timeouts <server>: the second lineup calls a tactical timeout after round
# three and a technical pause after round seven, which both teams resume.
timeouts() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:timeouts"
    start_match "$match"
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    check -v match="$match" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
map AS (SELECT * FROM match_maps WHERE match_id = :'match')
SELECT 'match status', 'Finished', (SELECT status FROM m)
UNION ALL
SELECT 'the lineup that called the timeout has one less', '2', (SELECT lineup_2_timeouts_available::text FROM map)
UNION ALL
SELECT 'the other lineup has all of theirs', '3', (SELECT lineup_1_timeouts_available::text FROM map)
UNION ALL
SELECT 'the last round carries both', '3-2',
       (SELECT lineup_1_timeouts_available || '-' || lineup_2_timeouts_available FROM match_map_rounds
         WHERE match_map_id = (SELECT id FROM map) AND deleted_at IS NULL ORDER BY round DESC LIMIT 1)
UNION ALL
SELECT 'the technical pause raised an alert', 'true',
       (SELECT (count(*) > 0)::text FROM notifications WHERE entity_id = :'match' AND title = 'Match Alert: Map Paused')
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# chat <server>: a player says glhf to the match after round one and talks to
# their own lineup after round two, and the other lineup says gg at the end.
chat() {
    local server=$1 match failed=0 lineup_1 lineup_2 speaker player all team other
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:chat"
    start_match "$match"
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    lineup_1=$(sql -c "SELECT lineup_1_id FROM matches WHERE id = '$match'")
    lineup_2=$(sql -c "SELECT lineup_2_id FROM matches WHERE id = '$match'")
    # Any of Bravo's players: the simulator picks the first of the roster the
    # api sends, in no set order.
    speaker=$(for ((player = 76561198000000000 + server * 10 - 4; player <= 76561198000000000 + server * 10; player++)); do
        printf '%s|' "$player"
    done)
    speaker=${speaker%|}
    all=$(redis HVALS "chat_match_$match")
    team=$(redis HVALS "chat_match_team_$match:$lineup_2")
    other=$(redis HVALS "chat_match_team_$match:$lineup_1")

    check -v speaker="$speaker" \
        -v glhf="$(grep -c '"glhf"' <<<"$all" || true)" \
        -v glhf_by="$(grep '"glhf"' <<<"$all" | grep -cE "$speaker" || true)" \
        -v gg="$(grep -c '"gg"' <<<"$all" || true)" \
        -v team="$(grep -c '"save this one"' <<<"$team" || true)" \
        -v team_in_all="$(grep -c '"save this one"' <<<"$all" || true)" \
        -v other="$(grep -c '"save this one"' <<<"$other" || true)" <<'SQL' || failed=1
SELECT 'glhf is in the match chat', '1', :'glhf'
UNION ALL
SELECT 'from the player who said it', '1', :'glhf_by'
UNION ALL
SELECT 'gg is in the match chat', '1', :'gg'
UNION ALL
SELECT 'the team line is in that lineup''s chat', '1', :'team'
UNION ALL
SELECT 'and not in the match chat', '0', :'team_in_all'
UNION ALL
SELECT 'nor in the other lineup''s', '0', :'other'
SQL
    release "$match"
    return "$failed"
}

# coach <server>: the second lineup has a coach, who connects but is neither a
# player for the ready-up nor rated.
coach() {
    local server=$1 match failed=0 coach in_lobby=false
    match=$(new_id)
    coach=$((76561198000000100 + server))
    create_match "$match" "$server" 1 "Alpha" "Bravo" coach=true
    start_match "$match"
    if until_true 300 in_lobby "$match" "$coach"; then
        in_lobby=true
    fi
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    check -v match="$match" -v coach="$coach" -v in_lobby="$in_lobby" -v server="$(server_id "$server")" <<'SQL' || failed=1
SELECT 'match status', 'Finished', (SELECT status FROM matches WHERE id = :'match')
UNION ALL
SELECT 'the coach connected to the match', 'true', :'in_lobby'
UNION ALL
SELECT 'the coach is not rated', '0', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match' AND steam_id = :'coach')
UNION ALL
SELECT 'every player is', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'the coach killed nobody', '0', (SELECT count(*)::text FROM player_kills WHERE match_id = :'match' AND attacker_steam_id = :'coach')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# camera <server>: cameras are required, and every player streams to mediamtx
# but one, whose camera only comes up once the api has paused the match for it.
camera() {
    local server=$1 match failed=0 first player late paused=false
    match=$(new_id)
    first=$((76561198000000000 + server * 10 - 9))
    late=$first
    create_match "$match" "$server" 3 "Alpha" "Bravo" camera_required=true
    for ((player = first; player <= first + 9; player++)); do
        if [ "$player" != "$late" ]; then
            tool camera-on "camera-$match-$player"
        fi
    done
    start_match "$match"

    if wait_for 600 "SELECT EXISTS (SELECT 1 FROM match_maps WHERE match_id = '$match' AND status = 'Paused')"; then
        paused=true
    fi
    tool camera-on "camera-$match-$late"
    wait_for 900 "$(settled "$match" "$server")" || echo "  (timed out waiting for the series to settle)"
    for ((player = first; player <= first + 9; player++)); do
        tool camera-off "camera-$match-$player" || true
    done

    check -v match="$match" -v late="$late" -v paused="$paused" -v server="$(server_id "$server")" \
        -v reported="$(docker compose logs "sim-$server" 2>/dev/null | grep -c "cameras offline: $late" || true)" <<'SQL' || failed=1
SELECT 'match status', 'Finished', (SELECT status FROM matches WHERE id = :'match')
UNION ALL
SELECT 'the api told the server who was off camera', 'true', (SELECT (:'reported'::int > 0)::text)
UNION ALL
SELECT 'which paused the match', 'true', :'paused'
UNION ALL
SELECT 'the pause raised an alert', 'true',
       (SELECT (count(*) > 0)::text FROM notifications WHERE entity_id = :'match' AND title = 'Match Alert: Map Paused')
UNION ALL
SELECT 'every player rated', '10', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# demo <server>: the server uploads a stand-in for the map's demo through the
# presigned url, as it would the .dem. Nothing parses it.
demo() {
    local server=$1 match failed=0 file stored=false
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo"
    start_match "$match"
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"
    wait_for 120 "SELECT EXISTS (SELECT 1 FROM match_map_demos WHERE match_id = '$match')" || true

    file=$(sql -c "SELECT file FROM match_map_demos WHERE match_id = '$match' LIMIT 1")
    if [ -n "$file" ] && docker compose run --rm -T --entrypoint /bin/sh buckets -c \
        "rc alias set local http://rustfs:9000 e2e-s3 e2e-s3-secret >/dev/null && rc stat 'local/5stack/$file'" >/dev/null 2>&1; then
        stored=true
    fi

    check -v match="$match" -v stored="$stored" <<'SQL' || failed=1
SELECT 'the map has its demo', '1',
       (SELECT count(*)::text FROM match_map_demos d JOIN match_maps mm ON mm.id = d.match_map_id WHERE mm.match_id = :'match')
UNION ALL
SELECT 'of the size uploaded', '1024', (SELECT size::text FROM match_map_demos WHERE match_id = :'match' LIMIT 1)
UNION ALL
SELECT 'stored in the bucket', 'true', :'stored'
SQL
    release "$match"
    return "$failed"
}

# ready_3v3 <server> <ready setting>: a 3v3 Competitive readies with its
# captains, or with an organizer's force_ready, and plays out unpaused.
ready_3v3() {
    local server=$1 ready=$2 match failed=0 forced=n/a started
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo" per_side=3 ready="$ready"
    # The no-show scenarios leave the same line in a server's log.
    started=$(date -u +%FT%TZ)
    start_match "$match"
    if [ "$ready" = Admin ]; then
        forced=false
        if until_true 300 sim_logged "$server" "waiting for force_ready" "$started"; then
            tool rcon "sim-$server" e2e-rcon force_ready >/dev/null && forced=true
        fi
    fi
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    check -v match="$match" -v forced="$forced" -v expected_forced="$([ "$ready" = Admin ] && echo true || echo n/a)" \
        -v server="$(server_id "$server")" <<'SQL' || failed=1
SELECT 'match status', 'Finished', (SELECT status FROM matches WHERE id = :'match')
UNION ALL
SELECT 'force_ready sent', :'expected_forced', :'forced'
UNION ALL
SELECT 'no round paused for missing players', '0',
       (SELECT count(*)::text FROM notifications WHERE entity_id = :'match' AND title = 'Match Alert: Map Paused')
UNION ALL
SELECT 'every player rated', '6', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# rush <server>: a 3v3 Rush, its one map won by the second lineup. Rush is
# first to 8 whatever mr says (MatchOptions.MaxRounds).
rush() {
    local server=$1 match failed=0
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:1-0" per_side=3 type=Rush
    start_match "$match"
    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    check -v match="$match" -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH m AS (SELECT * FROM matches WHERE id = :'match'),
map AS (SELECT mm.*, lineup_1_score(mm) AS l1, lineup_2_score(mm) AS l2 FROM match_maps mm WHERE mm.match_id = :'match')
SELECT 'match status', 'Finished', (SELECT status FROM m)
UNION ALL
SELECT 'the map goes to the lineup that had it', (SELECT lineup_2_id::text FROM m), (SELECT coalesce(winning_lineup_id::text, '') FROM m)
UNION ALL
SELECT 'on the rounds Rush plays to', '8', (SELECT l2::text FROM map)
UNION ALL
SELECT 'every player rated', '6', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}

# rush_restart <server>: the server stops mid-map in a Rush. Rush restores no
# rounds: the new server sets them aside and plays the map again from warmup,
# and refuses an admin's restore_round.
rush_restart() {
    local server=$1 match failed=0 map stopped_at restarted refusal="" logged
    match=$(new_id)
    create_match "$match" "$server" 1 "Alpha" "Bravo sim:1-0" per_side=3 type=Rush
    start_match "$match"

    wait_for 600 "SELECT count(*) >= 4 FROM match_map_rounds r JOIN match_maps mm ON mm.id = r.match_map_id
                   WHERE mm.match_id = '$match'" || echo "  (timed out waiting for the map)"
    docker compose kill "sim-$server" >/dev/null 2>&1
    stopped_at=$(sql -c "SELECT now()")
    map=$(sql -c "SELECT id FROM match_maps WHERE match_id = '$match'")
    restarted=$(date -u +%FT%TZ)
    docker compose up -d "sim-$server" >/dev/null 2>&1
    if until_true 120 sim_logged "$server" "setting up" "$restarted"; then
        refusal=$(tool rcon "sim-$server" e2e-rcon restore_round 2 || true)
    fi

    wait_for 600 "$(settled "$match" "$server")" || echo "  (timed out waiting for the map to finish)"
    logged=$(restores_to "$map" 0)

    check -v match="$match" -v map="$map" -v stopped_at="$stopped_at" -v logged="$logged" \
        -v refused="$(grep -c "round restores are disabled in Rush" <<<"$refusal" || true)" \
        -v server="$(server_id "$server")" <<'SQL' || failed=1
WITH alive AS (SELECT * FROM match_map_rounds WHERE match_map_id = :'map' AND deleted_at IS NULL)
SELECT 'match status', 'Finished', (SELECT status FROM matches WHERE id = :'match')
UNION ALL
SELECT 'the api set the recorded rounds aside', '1', (SELECT (:'logged'::int > 0)::int::text)
UNION ALL
SELECT 'every round kept was played after the restart', '0',
       (SELECT count(*)::text FROM alive WHERE "time" < :'stopped_at'::timestamptz)
UNION ALL
SELECT 'no round is recorded twice', (SELECT count(*)::text FROM alive), (SELECT count(DISTINCT round)::text FROM alive)
UNION ALL
SELECT 'an admin restore is refused', '1', :'refused'
UNION ALL
SELECT 'every player rated', '6', (SELECT count(*)::text FROM player_elo WHERE match_id = :'match')
UNION ALL
SELECT 'server released', 'true',
       (SELECT (reserved_by_match_id IS DISTINCT FROM :'match')::text FROM servers WHERE id = :'server')
SQL
    release "$match"
    return "$failed"
}
