using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ControlPanel.Core.Extensions;

public static class RegistryKeyExtension
{
    extension(RegistryKey registryKey)
    {
        public T[]? ReadArray<T>(string key)
            where T: unmanaged
        {
            if (registryKey.GetValue(key) is not byte[] buffer)
                return null;

            return [.. MemoryMarshal.Cast<byte, T>(buffer)];
        }

        public void WriteArray<T>(string key, ReadOnlySpan<T> data)
            where T: unmanaged
        {
            var buffer = MemoryMarshal.Cast<T, byte>(data).ToArray();
            registryKey.SetValue(key, buffer, RegistryValueKind.Binary);
        }
    }
}