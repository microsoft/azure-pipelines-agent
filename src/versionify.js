var fs = require('fs');
var path = require('path');

var argv = process.argv.slice(2);

if (argv.length <= 0)
{
    console.log('Error: You must supply a template');
    process.exit(1);
}

var version = fs.readFileSync(path.resolve(__dirname, 'agentversion'), 'utf8').trim();
var template = argv[0];
var destination = argv[1];

try
{
    var data = fs.readFileSync(template, 'utf8');
    data = data.replace(/<AGENT_VERSION>/g, version);
    if (destination)
    {
        console.log("Generating " + destination);
        fs.writeFileSync(destination, data);
    }
    else
    {
        console.log(data);
    }
}
catch(e)
{
    console.log('Error:', e.stack);
}