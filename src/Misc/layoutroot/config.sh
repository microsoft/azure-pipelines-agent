user_id=`id -u`

if [ $user_id -eq 0 ]; then
    echo "Must not run with sudo"
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

source ./env.sh

if [[ "$1" == "remove" ]]; then
    sudo ./bin/Agent.Listener unconfigure
else
    # user_name=`id -nu $user_id`

    sudo ./bin/Agent.Listener configure $*
fi
