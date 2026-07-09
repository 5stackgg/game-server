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

# SwiftlyS2 signatures are NOT baked in. main.py reads the SwiftlyS2.CS2 version pinned in
# apps/swiftly/src/FiveStack.csproj and fetches that version's gamedata at runtime. Baking it
# would go stale: swiftly-update.yaml bumps the pin on a schedule and its GITHUB_TOKEN push
# does not trigger a rebuild of this image.

ENTRYPOINT ["python", "main.py"]
