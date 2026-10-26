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

# series <server> <best of> <wins> <losses>: the second lineup takes the
# series wins-losses, the veto deciding the maps and sides.
series() {
    local server=$1 best_of=$2 wins=$3 losses=$4 match
    match=$(new_id)
    create_match "$match" "$server" "$best_of" "Alpha" "Bravo sim:$wins-$losses"
    wait_for 900 "$(settled "$match" "$server")" || echo "  (timed out waiting for the match to settle)"

    check -v match="$match" -v server="$(server_id "$server")" -v best_of="$best_of" \
        -v wins="$wins" -v losses="$losses" -v plan="$(series_plan "$wins" "$losses")" <<'SQL'
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
}
