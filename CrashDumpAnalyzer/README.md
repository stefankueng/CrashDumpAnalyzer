# CrashDumpAnalyzer

CrashDumpAnalyzer is a tool designed to automatically analyze crash dump files and organize them based on their callstacks.

By grouping similar crashes together, it helps developers focus on analyzing unique crashes rather than reviewing every individual dump.

Additionally, CrashDumpAnalyzer provides a symbol server to resolve symbols during the analysis process.

**Important**: This project is intended for use within a company network only and should not be exposed to the internet.

## Features
- dump files can be uploaded via web frontend or via REST-Api
- each dump is analyzed in the background, and once analysis is finished either a new report entry is created or the dump file is assigned to an existing entry matching the callstack of the crash
- Users can add comments to report entries, set a version where the crash is fixed and set a ticket/issue number
- integration with Jira
- basic log file analysis 


## Prerequisites
it is assumed that you can already analyze crash dumps of your application manually. This means you have set up a [symbol store](https://learn.microsoft.com/en-us/windows/win32/debug/using-symstore) where you store all necessary data for every build of your application. Also you should have configured your application with [SourceLink](https://github.com/dotnet/sourcelink).

CrashDumpAnalyzer needs access to your symbol store to properly analyze the crash dumps.



## Setting Up CrashDumpAnalyzer

- first, install the Windows SDK. Select only the debugging tools in the installer.
- 

