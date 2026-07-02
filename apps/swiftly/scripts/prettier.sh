# Define the path to the dotnet-csharpier executable
csharpier_dir="$HOME/.dotnet/tools"
csharpier_executable="$csharpier_dir/csharpier"

# Check if dotnet-csharpier executable exists
if [ -x "$csharpier_executable" ]; then
    echo "csharpier is already installed."
else
    echo "csharpier is not installed, installing..."

    # Install csharpier globally
    dotnet tool install -g csharpier

    # Check if installation was successful
    if [ $? -eq 0 ]; then
        echo "csharpier installed successfully."
    else
        echo "Error: Failed to install csharpier."
        exit 1
    fi
fi

# Check if the installed csharpier command is available
if [ ! -x "$csharpier_executable" ]; then
    echo "Error: csharpier executable not found. Make sure ~/.dotnet/tools is in your PATH."
    exit 1
fi

# Execute the csharpier command
$csharpier_executable format /opt/5stack
