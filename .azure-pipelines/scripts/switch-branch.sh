git config user.email "azure-pipelines-bot@microsoft.com"
git config user.name "azure-pipelines-bot"

git checkout -f origin/$TARGET_BRANCH

last_exit_code=$?

if [[ $last_exit_code != 0 ]]; then
    echo "git checkout failed with exit code $last_exit_code"
    exit 1
fi
