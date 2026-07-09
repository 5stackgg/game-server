FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends libstdc++6 && \
    rm -rf /var/lib/apt/lists/* && \
    pip install --no-cache-dir json5

COPY lib/s2binlib.so ./lib/s2binlib.so
COPY main.py s2binlib.py ./
COPY gamedata ./gamedata

ARG CCS_GAMEDATA_REF=main
RUN python -c "import urllib.request; urllib.request.urlretrieve('https://raw.githubusercontent.com/roflmuffin/CounterStrikeSharp/${CCS_GAMEDATA_REF}/configs/addons/counterstrikesharp/gamedata/gamedata.json', 'gamedata/ccs.gamedata.json')"

# SwiftlyS2 gamedata is NOT baked in. main.py reads the SwiftlyS2.CS2 version pinned in
# swiftly-game-server's FiveStack.csproj (the release we actually ship) and fetches that
# version's gamedata at runtime, so it tracks the source of truth instead of a ref pinned
# here that could drift. Runtime network access is required when validating the swiftlys2
# runtime; use --fivestack-ref to read a different branch/tag, or --swiftly-ref to override.

ENTRYPOINT ["python", "main.py"]
