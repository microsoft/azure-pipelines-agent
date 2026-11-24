# Register an Agent with Azure CLI Authentication

Use `--auth AZCLI` on Windows when Microsoft Entra Conditional Access requires
registration from a managed or compliant device. Azure CLI performs the user
sign-in; the Agent uses the resulting Azure DevOps token only during
registration.

## 1. Sign in with Azure CLI

Install Azure CLI, then run these commands as the same Windows user that will
run `config.cmd`:

```powershell
az config set core.enable_broker_on_windows=true
az login --tenant <tenant-id>
```

Complete the browser, MFA, and Conditional Access prompts.

Optionally verify access without printing the access token:

```powershell
az account get-access-token `
  --scope 499b84ac-1321-427f-aa17-267ca6975798/.default `
  --tenant <tenant-id> `
  --query "{tenant:tenant,expiresOn:expiresOn,tokenType:tokenType}"
```

## 2. Register the Agent

Extract the AZCLI-enabled Agent package and run:

```cmd
config.cmd ^
  --url https://dev.azure.com/<organization> ^
  --auth AZCLI ^
  --pool <pool-name> ^
  --agent <agent-name>
```

The user must have permission to register an Agent in the target pool.

The Azure CLI token is not stored by the Agent. Successful registration creates
the standard Agent OAuth/RSA identity used for subsequent connections.

## 3. Switch to an official Agent package

After registration, confirm that the temporary registration credential has
been replaced with the standard runtime credential:

```powershell
(Get-Content .credentials -Raw | ConvertFrom-Json).Scheme
```

The expected output is `OAuth`.

The preferred approach is to start the registered Agent and allow its built-in
self-update mechanism to install an official version.

If a manual switch is required:

1. Wait for running jobs to finish.
2. Stop the Agent service with `svc.cmd stop`, or close `run.cmd`.
3. Back up the complete Agent directory, including hidden files and ACLs.
4. Extract an official package for the same OS and architecture, such as
   `win-x64`, to a temporary directory.
5. Replace the program payload (`bin`, `externals`, and root scripts) in the
   existing Agent directory.
6. Preserve the existing `.agent`, `.credentials`,
   `.credentials_rsaparams`, `.service`, all other hidden configuration files,
   `_work`, and `_diag`.
7. Start the Agent and confirm it returns online and completes a test job.

Do not rerun the official package's `config.cmd`, delete the existing Agent
directory, or copy the registration files to another machine. The RSA identity
and protected credential data may be bound to the original machine, user, and
file permissions.

