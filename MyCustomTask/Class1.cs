// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace MyCustomTask
{
    public class Task_X : Task
    {
        public override bool Execute()
        {
            Log.LogMessage("Hello world!");

            var settings = new VerifySettings();
            settings.UseDirectory("Approvals");

            return true;
        }
    }
}
