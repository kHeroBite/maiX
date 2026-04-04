@echo off
dotnet build "C:\DATA\Project\mAIx\mAIx\mAIx.csproj" > "C:\DATA\Project\mAIx\build_output.txt" 2>&1
echo EXIT_CODE=%ERRORLEVEL% >> "C:\DATA\Project\mAIx\build_output.txt"
