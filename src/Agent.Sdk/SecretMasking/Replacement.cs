using System;

namespace Agent.Sdk.SecretMasking;

internal sealed class Replacement
{
    public Replacement(Int32 start, Int32 length, String token = "***")
    {
        Start = start;
        Length = length;
        Token = token;
    }

    public Replacement(Replacement copy)
    {
        Token = copy.Token;
        Start = copy.Start;
        Length = copy.Length;
    }
    public String Token { get; private set; }
    public Int32 Start { get; set; }
    public Int32 Length { get; set; }
    public Int32 End
    {
        get
        {
            return Start + Length;
        }
    }
}