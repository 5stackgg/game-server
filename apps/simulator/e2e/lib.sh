#!/usr/bin/env bash
# Helpers the scenarios share. Every SQL runs as hasura in the stack's Postgres.

sql() {
    docker compose exec -T -e PGOPTIONS="-c client_min_messages=warning" postgres \
        psql -U hasura -d hasura -v ON_ERROR_STOP=1 -X -q -At "$@"
}

new_id() {
    uuidgen | tr '[:upper:]' '[:lower:]'
}

# Server n hosts the scenario, with players 10n-9 .. 10n split five a side.
server_id() {
    echo "5e5e5e5e-0000-4000-8000-00000000000$1"
}

# create_match <match id> <server> <best of> <lineup 1 name> <lineup 2 name> [option=value...]
# Options: overtime (true), auto_cancel_duration and live_match_timeout (minutes,
# the api's defaults when unset). start_match puts it live.
create_match() {
    local match=$1 server=$2 best_of=$3 name_1=$4 name_2=$5 overtime=true auto_cancel="" live_timeout=""
    local per_side=5 ready=Players type=Competitive camera=false coach=false option
    shift 5
    for option in "$@"; do
        case "$option" in
            overtime=*) overtime=${option#*=} ;;
            auto_cancel_duration=*) auto_cancel=${option#*=} ;;
            live_match_timeout=*) live_timeout=${option#*=} ;;
            per_side=*) per_side=${option#*=} ;;
            ready=*) ready=${option#*=} ;;
            type=*) type=${option#*=} ;;
            camera_required=*) camera=${option#*=} ;;
            coach=*) coach=${option#*=} ;;
        esac
    done

    sql -v match="$match" -v server="$(server_id "$server")" -v best_of="$best_of" \
        -v name_1="$name_1" -v name_2="$name_2" -v overtime="$overtime" \
        -v auto_cancel="$auto_cancel" -v live_timeout="$live_timeout" \
        -v per_side="$per_side" -v ready="$ready" -v type="$type" -v camera="$camera" \
        -v coach="$( [ "$coach" = true ] && echo $((76561198000000100 + server)) )" \
        -v first=$((server * 10 - 9)) -v options="$(new_id)" \
        -v lineup_1="$(new_id)" -v lineup_2="$(new_id)" <<'SQL'
BEGIN;
-- The api runs the veto, one pick a second. Rush has a one-map pool and no veto.
INSERT INTO match_options (id, overtime, knife_round, mr, best_of, map_veto, veto_pick_timeout,
                           region_veto, regions, type, map_pool_id, tv_delay, prefer_dedicated_server,
                           auto_cancel_duration, live_match_timeout, ready_setting, camera_required, coaches)
VALUES (:'options', :overtime AND :'type' <> 'Rush', :'type' <> 'Rush', CASE WHEN :'type' = 'Rush' THEN 8 ELSE 12 END,
        :best_of, :'type' <> 'Rush', 1, false, '{Simulated}', :'type',
        (SELECT id FROM map_pools WHERE type = CASE WHEN :'type' = 'Rush' THEN 'Rush' ELSE 'Competitive' END AND seed LIMIT 1),
        115, true, nullif(:'auto_cancel', '')::int, nullif(:'live_timeout', '')::int, :'ready', :camera,
        nullif(:'coach', '') IS NOT NULL);

INSERT INTO match_lineups (id, team_name, coach_steam_id)
VALUES (:'lineup_1', :'name_1', NULL), (:'lineup_2', :'name_2', nullif(:'coach', '')::bigint);

INSERT INTO matches (id, status, match_options_id, lineup_1_id, lineup_2_id, server_id)
VALUES (:'match', 'Scheduled', :'options', :'lineup_1', :'lineup_2', :'server');

INSERT INTO match_lineup_players (match_lineup_id, steam_id, captain)
SELECT CASE WHEN i < :per_side THEN :'lineup_1'::uuid ELSE :'lineup_2'::uuid END,
       76561198000000000 + :first + i, i IN (0, :per_side)
FROM generate_series(0, 2 * :per_side - 1) i;
COMMIT;
SQL
}

# tools.mjs from inside the api container.
tool() {
    docker compose exec -T api node --input-type=module - "$@" <tools.mjs
}

start_match() {
    sql -c "UPDATE matches SET status = 'Live' WHERE id = '$1'" >/dev/null
}

# Puts the match in the first slot of a four-team bracket, which is all that
# makes it a tournament match. The tournament shares the match's options, which
# is where a tournament match reads its deadlines from, and is put Live with its
# triggers off: closing it for real redraws the bracket, and a closed one past
# its start is started, and with no teams cancelled, by the api.
make_tournament_match() {
    sql -v match="$1" <<'SQL'
BEGIN;
WITH t AS (
    INSERT INTO tournaments (name, start, organizer_steam_id, match_options_id, status)
    SELECT 'sim cup', now() - interval '1 minute', 76561198000000001, match_options_id, 'Setup'
      FROM matches WHERE id = :'match'
    RETURNING id
)
INSERT INTO tournament_stages (tournament_id, type, "order", min_teams, max_teams)
SELECT t.id, 'SingleElimination', 1, 4, 4 FROM t
RETURNING id AS stage, tournament_id AS tournament \gset

UPDATE tournament_brackets SET match_id = :'match'
 WHERE id = (SELECT id FROM tournament_brackets
              WHERE tournament_stage_id = :'stage' AND round = 1
              ORDER BY match_number LIMIT 1);

SET LOCAL session_replication_role = replica;
UPDATE tournaments SET status = 'Live' WHERE id = :'tournament';
COMMIT;
SQL
}

# wait_for <seconds> <sql that is true once done>
wait_for() {
    local deadline=$((SECONDS + $1)) query=$2
    until [ "$(sql -c "$query")" = "t" ]; do
        if [ "$SECONDS" -ge "$deadline" ]; then
            return 1
        fi
        sleep 2
    done
}

# The SQL on stdin returns one row per check: name|expected|actual. Prints a
# line per check and fails if any differ.
check() {
    local failed=0 name expected actual
    while IFS='|' read -r name expected actual; do
        if [ "$expected" = "$actual" ]; then
            echo "  ok    $name"
        else
            echo "  FAIL  $name: expected $expected, got $actual"
            failed=1
        fi
    done < <(sql "$@" -f -)
    return "$failed"
}

# What a series result looks like map by map for the lineup that has it:
# W and L, with the losses between the wins so it is decided on the last map.
series_plan() {
    local wins=$1 losses=$2 plan="" turn=W
    while [ "$wins" -gt 0 ] || [ "$losses" -gt 0 ]; do
        if [ "$wins" -gt 0 ] && { [ "$losses" -eq 0 ] || [ "$turn" = W ]; }; then
            plan+=W
            wins=$((wins - 1))
        else
            plan+=L
            losses=$((losses - 1))
        fi
        if [ "$turn" = W ]; then turn=L; else turn=W; fi
    done
    echo "$plan"
}
