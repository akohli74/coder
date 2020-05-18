# coder.net
This is a a customized TCP/IP stack written in .NET Core

Coded by Amrit Kohli (C) 2020, All rights reserved.

It highlights some important features of .NET Core, C#, and TCP/IP.

### .NET Core

  * I used the built-in IoC features of .NET Core, which can be seen in Program.cs of the Console app.
  * I used the built-in Configuration features of .NET Core, which can be seen throughout the application.
  * I used the built-in Logging features of .NET Core, which can seen throughout the application.
  * I used xunit to demonstrate the system working in what is more like an integration test, which can be found in ServerTests.cs.
  
### C#

  * I made use of some of the new features in C#, like switch expressions, as can be seen in Controller.cs
  * I make heavy use of the async/await pattern to improve the performance of the library.
  * I use the newish CancellationTokenSource pattern to stop Tasks that are running.
  
### TCP/IP

  * I created a Server (Listener) that will establish a connection with a client that initiates a connection to it.
  * I created a Client (Sender) that will initiate a connection to a server.
  * All of my communications are handled as asynchronous communications.
  
### Running Task
  * I created a new Base class for all "runnable" Tasks.  These are called "RunningTasks."  
  * The base class handles all the logic to Start, Stop, Restart, and Dispose of a "runnable" Task.

#### Limitations/Known Issues
  * Can only have one server and one client at a time.  Future versions will support a collection of Servers and Clients.
