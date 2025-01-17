// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.IO;

using SiliconStudio.Core.Extensions;
using SiliconStudio.Core.Settings;

namespace SiliconStudio.Assets
{
    public class PackageSettings
    {
        private const string SettingsExtension = ".pdxpkg.user";
        private readonly Package package;
        private readonly SettingsProfile profile;

        public static SettingsGroup SettingsGroup = new SettingsGroup();

        internal PackageSettings(Package package)
        {
            if (package == null) throw new ArgumentNullException("package");
            this.package = package;
            if (package.FullPath == null)
            {
                profile = SettingsGroup.CreateSettingsProfile(false);
            }
            else
            {
                var path = Path.Combine(package.FullPath.GetFullDirectory(), package.FullPath.GetFileName() + SettingsExtension);
                try
                {
                    profile = SettingsGroup.LoadSettingsProfile(path, false);
                }
                catch (Exception e)
                {
                    e.Ignore();
                }
                if (profile == null)
                    profile = SettingsGroup.CreateSettingsProfile(false);
            }
        }

        public bool Save()
        {
            if (package.FullPath == null)
                return false;

            var path = Path.Combine(package.FullPath.GetFullDirectory(), package.FullPath.GetFileName() + SettingsExtension);
            return SettingsGroup.SaveSettingsProfile(profile, path);
        }

        public SettingsProfile Profile { get { return profile; } }

        public T GetOrCreateValue<T>(SettingsValueKey<T> key)
        {
            return key.GetValue(true, profile, true);
        }

        public T GetValue<T>(SettingsValueKey<T> key)
        {
            return key.GetValue(true, profile);
        }

        public void SetValue<T>(SettingsValueKey<T> key, T value)
        {
            key.SetValue(value, profile);
        }
    }
}