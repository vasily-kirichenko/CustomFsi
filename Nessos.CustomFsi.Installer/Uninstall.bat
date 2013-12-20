@echo off
@setlocal enableextensions
@cd /d "%~dp0"

set current=%CD%s

@echo off
start .\CustomFsi.Installer.exe --uninstall