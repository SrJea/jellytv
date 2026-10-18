using System;
using System.Collections.Generic;
using System.Text;

namespace Jellyfin.Plugin.JellyChannels.Services;

/// <summary>
/// Utilidades pseudoaleatorias deterministas (SplitMix64 y FNV-1a), estables entre
/// versiones de .NET, para que la parrilla no cambie al actualizar el servidor.
/// </summary>
internal static class StableRandom
{
    private const ulong Golden = 0x9E3779B97F4A7C15;

    /// <summary>
    /// Hash FNV-1a de 64 bits de una cadena.
    /// </summary>
    /// <param name="value">Cadena.</param>
    /// <returns>Hash.</returns>
    public static long Hash(string value)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in Encoding.UTF8.GetBytes(value ?? string.Empty))
        {
            hash ^= b;
            hash *= 1099511628211;
        }

        return unchecked((long)hash);
    }

    /// <summary>
    /// Combina una semilla con un número de vuelta.
    /// </summary>
    /// <param name="seed">Semilla.</param>
    /// <param name="cycle">Vuelta.</param>
    /// <returns>Semilla derivada.</returns>
    public static long Mix(long seed, long cycle)
    {
        ulong state = unchecked((ulong)seed ^ ((ulong)cycle * Golden));
        return unchecked((long)Next(ref state));
    }

    /// <summary>
    /// Fisher-Yates determinista.
    /// </summary>
    /// <typeparam name="T">Tipo de elemento.</typeparam>
    /// <param name="source">Elementos.</param>
    /// <param name="seed">Semilla.</param>
    /// <returns>Copia barajada.</returns>
    public static T[] Shuffle<T>(IReadOnlyList<T> source, long seed)
    {
        var result = new T[source.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = source[i];
        }

        ulong state = unchecked((ulong)seed);
        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = (int)(Next(ref state) % (ulong)(i + 1));
            (result[i], result[j]) = (result[j], result[i]);
        }

        return result;
    }

    private static ulong Next(ref ulong state)
    {
        unchecked
        {
            ulong z = state += Golden;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }
}
