﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;
using System.IO;

using SiliconStudio.ExecServer;

namespace SiliconStudio.Assets.CompilerClient
{
    /// <summary>
    /// Small wrapper to communicate through ExecServer to launch Assets.CompilerApp.exe.
    /// The purpose of this small exe is to have the process name called "CompilerClient" instead
    /// of a generic name "ExecServer".
    /// </summary>
    public class Program
    {
        [LoaderOptimization(LoaderOptimization.MultiDomain)]
        public static int Main(string[] args)
        {
            const string CompilerAppExeName = "SiliconStudio.Assets.CompilerApp.exe";

            var serverApp = new ExecServerApp();
            var newArgs = new List<string>()
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CompilerAppExeName)
            };
            newArgs.AddRange(args);
            var result = serverApp.Run(newArgs.ToArray());
            return result;
        }
    }
}