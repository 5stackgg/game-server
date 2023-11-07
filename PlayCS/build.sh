apk add inotify-tools

directory_to_watch="/opt/playcs/PlayCS/bin/Debug/net7.0"

# Function to kill the dotnet watch build process
kill_dotnet_watch() {
  pkill -f "dotnet watch build"
  exit
}

# Start dotnet watch build in the background
dotnet watch build &

# Set up a trap to kill dotnet watch when the script exits
trap kill_dotnet_watch EXIT

# Start an infinite loop to monitor the directory
while true; do
  # Use inotifywait to watch for file changes
  inotifywait -r -e modify,create,delete,move "$directory_to_watch"
  echo "LETS GO!"

  rm $directory_to_watch/CounterStrikeSharp.API.dll

  chmod 777 "$directory_to_watch" -R
  cp $directory_to_watch/* /serverdata/serverfiles/game/csgo/addons/counterstrikesharp/plugins/PlayCS
done
