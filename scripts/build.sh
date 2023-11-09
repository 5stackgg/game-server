VERSION=$(grep '<Version>' ./PlayCS/PlayCS.csproj | sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p')

docker build -t lukepolo/playcs-server:beta-4 --platform linux/amd64 .