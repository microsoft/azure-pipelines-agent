# Process Flow  
1. Agent.Service.exe  
  * `src/Agent.Service/Windows/Program.cs`  
  * Creates and starts the `AgentService` COM service.  
    * `src/Agent.Service/Windows/AgentService.cs`  

2. AgentService.srv  
  * `src/Agent.Service/Windows/AgentService.cs`  
  * `OnStart` it starts the `Agent.Listener.exe` and waits until it exits.  
    * Execs `Agent.Listener.exe run --startuptype service`.  
  * Upon `Agent.Listener.exe exiting, it determines if it should restart the listener or not. This is needed so that if an unexpected error happens and the listener crashes, the service can just restart it if approapriate.  

3. Agent.Listener.exe  
  * `src/Agent.Listener/Program.cs`  
  * A `CommandSettings` object is created with the passed in args.  
    * The args were `run --startuptype service`.  
  * The `Agent` service is created.  
    * `src/Agent.Listener/Agent.cs`  
  * The `CommandSettings` are passed to the `Agent` to Process. See Program.cs line #137.  

4. Agent.srv  
  * Connects to VSTS.  
  * `Agent.RunAsync()` is called  
    * A `MessageListener` session is created for this agent.  
      * `src/Agent.Listener/MessageListener.cs`  
    * A `JobDispatcher` is created.  
      * `src/Agent.Listener/JobDispatcher.cs`  
    * An update loop is started here, where the agent pulls messages until the agent hears a cancelation request.  
      * See Agent.cs line #253  
    * Upon getting a `AgentJobRequestMessage`, it tells the `JobDispatcher` to `Run()` the job.  
      * See Agent.cs line #335

5. JobDispatcher.srv
  * Only executes one job at a time. Agent.srv awaits on this when it calls into it.  
  * `JobDispatcher.Run()`
    * Asyncronously runs the job by calling `RunAsync`, and stores the jobID and other IDs so the calling code can check the status of the job.  
  * `JobDispatcher.RunAsync()`
    * (not important) Starts a "renew" job request by calling `RenewJobRequestAsync()`  
      * Asks the `AgentServer` to renew the job for a given pool by calling `AgentServer.RenewAgentRequestAsync()`  
      * This process seems to only be needed to ensure there's still a valid http connection to the VSTS server, we should just ignore this.  
    * Creates `ProcessInvoker` and `ProcessChannel` services (used for starting and communicated with process' through named pipes.  
    * Spawns the `Agent.Worker.exe` process with arguments `spawnclient {output_pipe} {input_pipe}.
    * Sends the `NewJobRequest` through the named pipe to the worker.

6. Agent.Worker.exe
  * ... long story short, a whole bunch of tasks get populated, and we end up in the "CheckoutTask"...


