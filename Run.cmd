@echo off
cd /d "%~dp0"
dotnet build --nologo
if errorlevel 1 (
    echo Build failed. See the output above.
    pause
    exit /b 1
)
start "" "D:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0."
