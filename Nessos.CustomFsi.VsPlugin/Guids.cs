// Guids.cs
// MUST match guids.h
using System;

namespace Nessos.CustomFsi.VsPlugin
{
    static class GuidList
    {
        public const string guidNessos_CustomFSIPkgString = "adff2b7c-9847-421c-9598-b378536cc3c4";
        public const string guidNessos_CustomFSICmdSetString = "9e011a04-13a9-49e5-8e61-262dca8f3133";
        public const string guidToolWindowPersistanceString = "2ea1e2b0-95d4-4a87-b5de-ed97e58aaaf6";

        public static readonly Guid guidNessos_CustomFSICmdSet = new Guid(guidNessos_CustomFSICmdSetString);
    };
}