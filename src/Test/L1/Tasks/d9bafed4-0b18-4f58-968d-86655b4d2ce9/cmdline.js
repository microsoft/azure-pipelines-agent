"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const fs = require("fs");
const path = require("path");
const tl = require("azure-pipelines-task-lib/task");
var uuidV4 = require('uuid/v4');
function run() {
    return __awaiter(this, void 0, void 0, function* () {
        try {
            tl.setResourcePath(path.join(__dirname, 'task.json'));
            // Get inputs.
            let failOnStderr = tl.getBoolInput('failOnStderr', false);
            let script = tl.getInput('script', false) || '';
            let workingDirectory = tl.getPathInput('workingDirectory', /*required*/ true, /*check*/ true);
            // Write the script to disk.
            console.log(tl.loc('GeneratingScript'));
            tl.assertAgent('2.115.0');
            let tempDirectory = tl.getVariable('agent.tempDirectory');
            tl.checkPath(tempDirectory, `${tempDirectory} (agent.tempDirectory)`);
            let filePath = path.join(tempDirectory, uuidV4() + '.sh');
            yield fs.writeFileSync(filePath, script, // Don't add a BOM. It causes the script to fail on some operating systems (e.g. on Ubuntu 14).
            { encoding: 'utf8' });
            // Print one-liner scripts.
            if (script.indexOf('\n') < 0 && script.toUpperCase().indexOf('##VSO[') < 0) {
                console.log(tl.loc('ScriptContents'));
                console.log(script);
            }
            // Create the tool runner.
            console.log('========================== Starting Command Output ===========================');
            let bash = tl.tool(tl.which('bash', true))
                .arg('--noprofile')
                .arg(`--norc`)
                .arg(filePath);
            let options = {
                cwd: workingDirectory,
                failOnStdErr: false,
                errStream: process.stdout,
                outStream: process.stdout,
                ignoreReturnCode: true
            };
            // Listen for stderr.
            let stderrFailure = false;
            const aggregatedStderr = [];
            if (failOnStderr) {
                bash.on('stderr', (data) => {
                    stderrFailure = true;
                    // Truncate to at most 10 error messages
                    if (aggregatedStderr.length < 10) {
                        // Truncate to at most 1000 bytes
                        if (data.length > 1000) {
                            aggregatedStderr.push(`${data.toString('utf8', 0, 1000)}<truncated>`);
                        }
                        else {
                            aggregatedStderr.push(data.toString('utf8'));
                        }
                    }
                    else if (aggregatedStderr.length === 10) {
                        aggregatedStderr.push('Additional writes to stderr truncated');
                    }
                });
            }
            // Run bash.
            let exitCode = yield bash.exec(options);
            let result = tl.TaskResult.Succeeded;
            // Fail on exit code.
            if (exitCode !== 0) {
                tl.error(tl.loc('JS_ExitCode', exitCode));
                result = tl.TaskResult.Failed;
            }
            // Fail on stderr.
            if (stderrFailure) {
                tl.error(tl.loc('JS_Stderr'));
                aggregatedStderr.forEach((err) => {
                    tl.error(err);
                });
                result = tl.TaskResult.Failed;
            }
            tl.setResult(result, null, true);
        }
        catch (err) {
            tl.setResult(tl.TaskResult.Failed, err.message || 'run() failed', true);
        }
    });
}
run();
