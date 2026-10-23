using System;
using UnityEngine;

[Serializable]
public class LocalizationKey
{
    [SerializeField]
    private string key;

    public string Key => key;

    public override string ToString()
    {
        return key;
    }

    public static implicit operator string(LocalizationKey k)
    {
        return k?.key;
    }

    public void Set(string value)
    {
        key = value;
    }
}