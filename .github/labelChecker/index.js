const rm = require('typed-rest-client/RestClient');
const core = require('@actions/core');
const github = require('@actions/github');

async function main() {
    try {
        const issueTypes = ['bug', 'enhancement', 'misc', 'internal'];
        const pullRequestNumber = github.context.issue.number;
        console.log(`Running for PR: ${pullRequestNumber}\n`);
        let rest = new rm.RestClient('labelChecker');
        console.log('Getting label info\n');
        let res = await rest.get(`https://api.github.com/repos/microsoft/azure-pipelines-agent/issues/${pullRequestNumber}/labels`);
        console.log(`Labels: ${JSON.stringify(res.result)}`);
        let labelCount = 0;
        res.result.forEach(tag => {
            let name = tag.name.toLowerCase();
            if (issueTypes.indexOf(name) > -1) {
                console.log(`Found tag: ${name}`);
                labelCount++;
            }
        });

        if (labelCount === 0) {
            throw `Must be labeled one of ${issueTypes.join(', ')}`
        }
        if (labelCount > 1) {
            throw `Cannot contain more than one label of ${issueTypes.join(', ')}. Currently contains ${labelCount}`
        }
    } catch (err) {
        core.setFailed(err);
    }

}

main();
