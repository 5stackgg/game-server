apk add inotify-tools

# Variable to store the PID of dotnet watch process
dotnet_watch_pid=""

# Function to kill the dotnet watch build process
kill_dotnet_watch() {
  if [ -n "$dotnet_watch_pid" ]; then
    kill "$dotnet_watch_pid"
    exit
  fi
}

read -p "Enter Server ID: " server_id

# Set Server ID as an environment variable
export SERVER_ID=$server_id

echo "Server ID set as environment variable: SERVER_ID=$SERVER_ID"
echo "SERVER_ID=$SERVER_ID" > /serverdata/serverfiles/.env

dotnet watch build --project src &
dotnet_watch_pid=$!

# Set up trap to kill dotnet watch process on script exit
trap kill_dotnet_watch EXIT

directory_to_watch="/opt/5stack/src/bin/Debug/net7.0"

while true; do
  inotifywait -r -e modify,create,delete,move "$directory_to_watch"

  rm "$directory_to_watch/CounterStrikeSharp.API.dll"

  cp "$directory_to_watch"/* "/opt/dev"
done
