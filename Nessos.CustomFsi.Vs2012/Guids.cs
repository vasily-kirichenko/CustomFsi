// Guids.cs
// MUST match guids.h
using System;

namespace Nessos.CustomFsi.Vs2012
{
    static class GuidList
    {
        public const string guidVsPluginPkgString = "9cf2e4d2-fa2e-4e55-9af0-185783ea2dc7";
        public const string guidVsPluginCmdSetString = "5630453b-792d-47db-af2a-ff9411aaf8de";
        public const string guidToolWindowPersistanceString = "2308696f-7f05-4a12-bf26-cd577d534c90";

        public static readonly Guid guidVsPluginCmdSet = new Guid(guidVsPluginCmdSetString);
    };
}