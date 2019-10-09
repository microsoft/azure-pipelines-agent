# Elastic Private Agent Pools (formerly "BYOS: Bring Your Own Subscription Agent Pools")

Hosted agents are extremely convenient: low/no cost, no infrastructure to maintain, and elastic with demand.
In return, customers must give up control over tools, capacity, and speed.
On the other end of the spectrum, private agents offer the exact opposite set of trade-offs: full control over everything, at the expense of maintaining and paying for infrastructure.
With private agents, elasticity is difficult to achieve.

Bring Your Own Subscription (BYOS) represents a middle ground:
it pairs the convenience and elastic capacity of the hosted pool with the control and flexibility of private agents.
**Azure Pipelines will manage a set of build/release agents to the customer's specification, completely automated, in the customer's Azure subscription.**

## State of this spec

This is in the early design phase and we are looking for feedback in the PR (or as issues in the repo).

## Customer scenarios

The theme throughout these scenarios is that the customer wants hosted elasticity but customization beyond what Hosted offers.

General themes are around:
- VM specs (memory, CPU, disk) and network environment
- Preinstalled software
- Agent reuse
- On-premises customers have often asked us for access to the Hosted pools.
In lieu of that (which complicates billing and technical design), this feature must be available to Azure DevOps Server customers.

### VM specs and environment

1. Customer wants more memory, more processor, or more IO than our native images.
2. [Customer](https://github.com/MicrosoftDocs/vsts-docs/issues/2985) wants an NCv2 VM with particular instruction sets for machine learning. (It's niche enough that we won't stand up dedicated hosted pools, but broad enough to be very interesting for our business.)
3. Customer wants additional storage attached to the VM. *(Real scenario from a medium-sized customer)*
4. Customer wants to deploy to a private App Service. It's in a private VNET with no inbound connectivity. Today, this customer is forced to keep private agents standing by.

### Preinstalled software

1. Customer wants Windows 2016 with VS 2019 Preview. (We only offer certain combos like Win2016 + VS2017 and Win2019 + VS2019.)
2. Customer wants to pin a specific set of tools and dependencies, preconfigured on the image.
3. Customer wants extreme control over the exact OS build, settings, versions, and so on.

### Agent reuse

1. Customer wants to run several consecutive jobs on an agent to take advantage of incremental source and machine-level package caches.
They want to specify minimum and maximum # of agents associated with time ranges or # of builds, but reuse agents within that span.
This can save money by not running unnecessary VMs overnight when there's no load.
It can also increase reliability: we're aware of customers who need to blow away agents once every N builds (or after certain kinds of tests) because the accumulated "detritus" makes subsequent builds flaky.
3. Customer wants to run additional configuration or cache warmup before an agent beings accepting jobs.
As additional agents are spun up, the customer has an opportunity to run a prep script that doesn't impact "pipeline runtime".
This is akin to the hosted pool's provisioner script.
_While it's really nice to have, we could imagine shipping without it._

### On-premises customers

1. Customer wants to use Azure DevOps Server with elastic agent pools.

## Industry review

Similar problem spaces:
- [Jenkins can use Azure agents](https://docs.microsoft.com/en-us/azure/jenkins/jenkins-azure-vm-agents) this way
- [AppVeyor](https://www.appveyor.com/docs/enterprise/running-builds-on-azure/) offers instructions for solving a similar problem on several cloud providers
- GitLab CI/CD offers auto-scaling of builder containers using [Docker Machine](https://gitlab.com/gitlab-org/gitlab-runner/blob/master/docs/configuration/autoscale.md) or [Kubernetes](https://docs.gitlab.com/runner/executors/kubernetes.html).

Not offered:
- [Travis CI](https://docs.travis-ci.com/user/enterprise/setting-up-travis-ci-enterprise/) offers an enterprise product that you can install on your own infrastructure. While you can choose what kind of VM to run it on, there's no elasticity.
- [CircleCI](https://circleci.com/docs/2.0/aws/#nomad-clients) offers an on-your-infrastructure product. You must scale up and down the workers manually, there's no elasticity.

## Solution

For starters, this is about running agents on VMs in Azure.
Primarily VM scale sets.
Later, we may consider whether this same solution works for:
- AKS
- Any Kubernetes
- Other clouds

Under the hood, we need an image, an ARM template, an Azure subscription, and instructions about how much capacity to provision.
That doesn't require us to expose the full complexity of ARM templates and Azure subscriptions to every customer.

### Setup

For a lot of customers, it would be enough to have them
- go to pool setup
- create a new pool of type "Custom Azure-hosted" (name t.b.d.)
- pick one of a few different Azure VM SKUs + a few different agent lifetime policies
- pick their Azure subscription
- have Azure Pipelines configure it all for them

For the advanced customer who really needs all the customization available, they'll switch to actually giving us the ARM template and so on.

_TODO_: draw pictures

### Infrastructure problems

_TODO_: How will we let the adminstrator know of any problems that occur?
Azure Portal will report things, but the leading indicator is probably "my builds aren't running".

## Alternatives considered

### Custom / configurable SKU

Offering additional Azure SKUs in Hosted will meet some of the above scenarios.
Specifically, if all you need are additional resources (memory, compute, etc.), then a custom SKU works.

Pros:
- Easiest for the customer to understand
- Potential business model around premium SKUs

Cons:
- Capacity planning and buildout becomes a lot more complex
- If a customer wants a SKU we don't offer, they're out of luck until we add it
- Does not offer custom elasticity, network environment, or image

### Bring your own cloud (pool providers)

Putting the entire agent alloc/release cycle in the hands of the customer offers them ultimate flexibility.

Pros:
- Maximum flexibility
- Customer can select other infrastructure: on-prem, in a different hosting provider, etc.

Cons:
- Much heavier burden to understand, set up, and maintain
- Customer has to swallow full complexity load to get any flexibility

<!--
older section
## Goals

- **Fully automated dedicated agents with elasticity**: User configures contraints and we provision, start and stop the agents.
- **Customer control of image and toolsets**: Pick the image to use.  Stay on it until you change the configuration.  Use our published images that we release monthly.
- **Control machine configurations**: User can provide VM SKU and other configuration options (provide ARM).
- **Control agent lifetime**: Agents can be single use, or thrown away on a configured interval (nightly, etc).
- **Incremental sources and packages**: Even if you choose single use, we can warm up YAML run when bringing VM online. 
- **Cached container images on host**: Ensure a set of container images are cached on the host via warmup YAML.
- **Maintenance**: Schedule maintenance jobs for pruning repos, OS security updates, etc.
- **Elastic pools for VSTS and On-prem**:  Use elastic Azure compute as build resources for VSTS but also on-prem TFS.
- **Allow domain joined and on-prem file shares**: Leverage AAD and Express Routes for elastic on-prem scenarios.
- **Configure multiple pools of type BYOS**: Allows for budgeting of resources across larger enterprise teams.
- **Control costs**: Stop agents when not in use to control Azure charges

## Design

Pending on goals discussions.
-->
