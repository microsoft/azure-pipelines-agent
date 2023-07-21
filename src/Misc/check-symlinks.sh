cd $(dirname $0)/../../_layout
brokenSymlinks=$(find . -type l ! -exec test -e {} \; -print)
if [[ $brokenSymlinks != "" ]]; then
    printf "Broken symlinks exist in the agent build:\n$brokenSymlinks\n"
    exit 1
fi
echo "Broken symlinks not found in the agent build."
