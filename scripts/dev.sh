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

dotnet watch build &
dotnet_watch_pid=$!

Set up trap to kill dotnet watch process on script exit
trap kill_dotnet_watch EXIT

directory_to_watch="/opt/playcs/bin/Debug/net7.0"

while true; do
  inotifywait -r -e modify,create,delete,move "$directory_to_watch"

  rm "$directory_to_watch/CounterStrikeSharp.API.dll"

  chmod 777 "$directory_to_watch" -R
  cp "$directory_to_watch"/* "/serverdata/serverfiles/game/csgo/addons/counterstrikesharp/plugins/PlayCS"
done
