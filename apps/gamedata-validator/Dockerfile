FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends libstdc++6 && \
    rm -rf /var/lib/apt/lists/* && \
    pip install --no-cache-dir json5

COPY apps/gamedata-validator/lib/s2binlib.so ./lib/s2binlib.so
COPY apps/gamedata-validator/main.py apps/gamedata-validator/s2binlib.py ./
COPY shared/gamedata ./gamedata

ARG CCS_GAMEDATA_REF=main
RUN python -c "import urllib.request; urllib.request.urlretrieve('https://raw.githubusercontent.com/roflmuffin/CounterStrikeSharp/${CCS_GAMEDATA_REF}/configs/addons/counterstrikesharp/gamedata/gamedata.json', 'gamedata/ccs.gamedata.json')"

# SwiftlyS2 gamedata is NOT baked in. main.py reads the SwiftlyS2.CS2 version pinned in
# apps/swiftly's FiveStack.csproj (the release we actually ship) and fetches that version's
# gamedata at runtime, so it tracks the source of truth instead of a ref pinned here that
# could drift -- swiftly-update.yaml bumps that pin on a schedule, and its GITHUB_TOKEN push
# does not trigger a rebuild of this image. Fetching gamedata needs the network either way.
# Use --fivestack-ref to read a different branch/tag, or --swiftly-ref/--swiftly-version to override.

ENTRYPOINT ["python", "main.py"]
