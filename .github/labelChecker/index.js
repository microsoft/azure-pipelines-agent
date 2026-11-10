const core = require('@actions/core');
const github = require('@actions/github');

function main() {
    try {
        const issueTypes = ['bug', 'enhancement', 'misc', 'internal'];
        const pullRequestNumber = github.context.issue.number;
        console.log(`Running for PR: ${pullRequestNumber}\n`);

        const pullRequest = github.context.payload && github.context.payload.pull_request;
        const labels = pullRequest && pullRequest.labels;
        if (!Array.isArray(labels)) {
            throw new Error('The pull request event payload did not contain labels.');
        }

        console.log(`Labels: ${JSON.stringify(labels)}`);
        let labelCount = 0;
        labels.forEach(tag => {
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
