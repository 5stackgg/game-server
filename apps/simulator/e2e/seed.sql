\set ON_ERROR_STOP on
-- A region, a server row per simulator in compose.yml, and ten players for
-- each of them, so matches on different servers never share a player.
BEGIN;
INSERT INTO server_regions (value, description, is_lan) VALUES ('Simulated', 'Simulated', false);

INSERT INTO servers (id, host, label, rcon_password, port, tv_port, api_password, type, is_dedicated, enabled, region)
SELECT ('5e5e5e5e-0000-4000-8000-00000000000' || n)::uuid, 'sim-' || n, 'simulator ' || n, 'e2e-rcon'::bytea, 27015, 27020,
       ('5e5e5e5e-0000-4000-8000-0000000000a' || n)::uuid, 'Ranked', true, true, 'Simulated'
FROM generate_series(1, 3) n;

INSERT INTO players (steam_id, name, role)
SELECT 76561198000000000 + i, 'sim-' || i, 'user' FROM generate_series(1, 30) i;
COMMIT;
