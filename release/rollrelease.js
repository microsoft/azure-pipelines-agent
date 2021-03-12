
const { Octokit } = require("@octokit/rest");
const owner = 'microsoft';
const repo  = 'azure-pipelines-agent';

var opt = require('node-getopt').create([
    ['',  'dryrun',           'Dry run only, do not actually commit new release'],
    ['',  'ghpat=pat',        'GitHub PAT', ''],
    ['',  'stage=stage',      'The stage of the current agent deployment (ex. Ring 0)', ''],
    ['h', 'help',             'Display this help'],
  ])
  .setHelp(
    "Usage: node rollrelease.js [OPTION] <version>\n" +
    "\n" +
    "[[OPTIONS]]\n"
  )
  .bindHelp()     // bind option 'help' to default action
  .parseSystem(); // parse command line



async function main()
{
    var release = opt.argv[0];
    if (release === undefined)
    {
        console.log('Error: You must supply a version');
        process.exit(-1);
    }

    if (!opt.options.stage)
    {
        console.log('Error: You must specify the stage the agent is being rolled out to');
        process.exit(-1);
    }

    const octokit = new Octokit({
        auth: opt.options.ghpat
    });

    var tag = 'v' + release;
    var releaseInfo;
    try
    {
        releaseInfo = await octokit.repos.getReleaseByTag({
            owner,
            repo,
            tag
        });
    }
    catch (e)
    {
        console.log(`Error: Unable to find release ${tag}: ${e}`);
        process.exit(-1);
    }

    var releaseId = releaseInfo.data.id;

    // TODO: Add other actions to take when rolling agent to specific rings
    //   Some ideas:
    //      - Update release body
    //      - Post to Slack Channel

    if (opt.options.stage.toLowerCase() === 'ring 5')
    {
        if (!opt.options.dryrun)
        {
            try
            {
                await octokit.repos.updateRelease({
                    owner,
                    repo,
                    release_id: releaseId,
                    prerelease: false,
                });

                console.log(`Release ${release} marked no longer pre-release`);
            }
            catch (e)
            {
                console.log(`Error: Problem updating release: ${e}`);
            }
        }
        else
        {
            console.log(`Release ${release} to be marked no longer pre-release`);
        }
    }
}

main();