# ![win](../res/win_med.png) Windows System Pre-requisites

## Windows 10 and Beyond (64-bit)

No known system pre-requisistes are known at this time.

## Windows 7 to Windows 8.1 and Windows Server 2012 R2 (64-bit)

[Updated C runtime is needed](https://support.microsoft.com/en-us/kb/2999226)

## MSBuild & .Net SDK

Since you want to ensure your build agent does not have Visual Studio installed, you will need the .Net SDK, which includes MSBuild so that you can build all your projects on a server without Visual Studio.  Keeping Visual Studio off of your build agent helps avoid tricky environment dependencies that can be a big surprise when deploying to a downstream environment.  
