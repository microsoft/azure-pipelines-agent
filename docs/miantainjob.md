# Agent Maintain Job

Provide the ability for customer to execute some maintain operations on the agent.    

## Problems we have today
    - The agent is eating up all my disk space after running some builds.
    - There is no indicator about agent run out of disk space, all my builds suddenly failed without log, take long time to figure out disk space is causing problem.

## Maintain operations

    - Cleanup un-used build source directory.  
            Agent current store your source repository on disk based on build definition uri and repository id.  
            If your agent have built lots of different definitions and repositories, but you no long need the agent to build those repositories or definition.  
            You want a way to free up the disk space based on how long did the agent use the stored source folder since last been used, 7/30/90 days.  

    - Cleanup orphan build source directory.  
            Agent current store your source repository on disk based on build definition uri and repository id.  
            If your ever change your definition to build a different repository, then the old stored source directory on disk will be marked as GC since it no longer needed.    
            You want a way to free up the disk space for this kind of usage.  
    
    - Cleanup task download folder.  
            Agent current store store any version of any task you have used, as some tasks may contain big binary file, you may want to delete those old task cache to save disk space.    
            During the cleanup, we will only keep the latest version of each major version for every task.  

    - Publish disk usage report.
            We will print out your agent's work folder size to the log, along with the size of volume where the work folder located at.  
            We can log a warning when the disk usage reach certain threshold, like 90%.    

## Server side design

Maintain job will reuse the existing DistributedTask infastructure for reserve agent, job request distibution and log upload.  
The maintain job configuration is at agent pool level, we will add new UI in the pool admin page for maintain configuration.  
Customer will configure the maintain setting for each pool though UI, and the maintain job will run through all agents within that pool.  
Customer can choose to manually run the maintain job on a given pool, or set the maintain job to run recurrently, Daily/Weekly/Monthly.

The configuration will be a JSON blob, here is a sample of the maintain job configuration JSON:
```JSON
{    
    "MaintainOptions" : {
        "CleanBuildDirectory" : "true",
        "DaysToTreatAsUnusedBuildDirectory" : "7",
        "CleanTaskDownloadDirectory" : "true"
    },

    "Recurrence" : "Monthly",
    
    "MaintainJobRetention" : "10",

    "MaintainJobTimeout" : "30",

    "MaxAgentsConcurrent" : "2"
}
```
`MaintainOptions`: maintain option that agent will follow.  
`Recurrence`: run maintain job automatically every month.  
`MaintainJobRetention`: number of maintain run's record to keep in server.  
`MaintainJobTimeout`: the max number of mins a maintain job can run on an agent.  
`MaxAgentsConcurrent`: the max number of agents the maintain job will take a given time.   

Introduce new REST endpoints for maintain job.

1. Return all maintain jobs run on this pool. The returned JSON contains all maintain job runs and agents for each run.  
Get: `https://{account}.visualstudio.com/_apis/DistributedTask/pools/{poolId}/maintaion`
    
2. Run maintain job on a given pool.  
Post: `https://{account}.visualstudio.com/_apis/DistributedTask/pools/{poolId}/maintaion`

3. Get log for a given maintain job execution.  
Get: `https://{account}.visualstudio.com/_apis/DistributedTask/pools/{poolId}/maintaion/{jobId}/log`

## Agent side design

Since the maintain job is just a different type of job, so the agent only need to add a `MaintainJobExtension` for it, just like `BuildJobExtension` or `ReleaseJobExtension`.
We will also introduce a new extension called `IMaintainServiceProvider`  
```C#
public interface IMaintainServiceProvider : IExtension
{
    string Description { get; }
    Task RunMaintainServiceAsync(IExecutionContext context);
}
```
During a maintain job, the `PrepareStep` of `MaintainJobExtension` will loop through all different MaintainServiceProviders and execute `RunMaintainServiceAsync()` on each of them.  
During a maintain job, the `FinallyStep` of `MaintainJobExtension` will inspect the disk space used by the agent and log an warning on high disk space usage.  

## E2E workflow

1. Customer enable maintain job for a given pool, the maintain job is turn off by default.
2. When customer queue a maintain job for the pool or the maintain job recurrently run happens, server will send the maintain job to all online and enabled agents.
3. When maintain job finish, customer can view the maintain log upload from each agents, the UI will also highlight whether there is a warning or error during the maintain job to bring customer's attention.
4. On failure, customer can logon to the machine to run further cleanup base on the maintain log. 
