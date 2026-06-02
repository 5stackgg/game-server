FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends libstdc++6 && \
    rm -rf /var/lib/apt/lists/* && \
    pip install --no-cache-dir commentjson

COPY lib/s2binlib.so ./lib/s2binlib.so
COPY main.py s2binlib.py ./
COPY gamedata ./gamedata

ARG CCS_GAMEDATA_REF=main
RUN python -c "import urllib.request; urllib.request.urlretrieve('https://raw.githubusercontent.com/roflmuffin/CounterStrikeSharp/${CCS_GAMEDATA_REF}/configs/addons/counterstrikesharp/gamedata/gamedata.json', 'gamedata/ccs.gamedata.json')"

ENTRYPOINT ["python", "main.py"]
