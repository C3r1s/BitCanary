namespace Messenger.Application.Abstractions;

public interface ISpkValidator
{
    bool Validate(byte[] ikPublic, byte[] spkPublic, byte[] spkSignature);
}
