user_id=`id -u`

if [ $user_id -eq 0 ]; then
    echo "Must not run interactively with sudo"
    exit 1
fi

if [ ! -f .Agent ]; then
    echo "Must configure first. Run ./config.sh"
    exit 1
fi

# Ensure the execute bit is set for Agent.Listener.
if [ ! -x ./bin/Agent.Listener ]; then
    echo chmod +x ./bin/Agent.Listener
    chmod +x ./bin/Agent.Listener
fi

# Ensure the execute bit is set for Agent.Worker.
if [ ! -x ./bin/Agent.Worker ]; then
    echo chmod +x ./bin/Agent.Worker
    chmod +x ./bin/Agent.Worker
fi

./bin/Agent.Listener $*