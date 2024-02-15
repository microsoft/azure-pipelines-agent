git config user.email "azure-pipelines-bot@microsoft.com"
git config user.name "azure-pipelines-bot"

git checkout -f origin/$env:TARGET_BRANCH

if ($LASTEXITCODE -ne 0){
    Write-Error "git checkout failed with exit code $LASTEXITCODE" -ErrorAction Stop
}
