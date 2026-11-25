using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace BlueID.Infrastructure.Security;

/// <summary>
/// Encrypts the ASP.NET Core Data Protection key ring (which protects session cookies) at rest with the master key
/// (PROMPT 7: secrets encrypted when stored). Keys written before this existed stay readable; new keys are encrypted.
/// </summary>
public sealed class MasterKeyXmlEncryptor(MasterKey master) : IXmlEncryptor
{
    public const string Purpose = "data-protection";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var data = master.Encrypt(Purpose, Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting)));
        var element = new XElement("encryptedKey",
            new XComment(" Encrypted with BID_MASTER_KEY (AES-256-GCM), docs/03 "),
            new XElement("value", Convert.ToBase64String(data)));
        return new EncryptedXmlInfo(element, typeof(MasterKeyXmlDecryptor));
    }
}

/// <summary>Created by Data Protection through its (IServiceProvider) constructor.</summary>
public sealed class MasterKeyXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var master = services.GetRequiredService<MasterKey>();
        var value = encryptedElement.Element("value")?.Value ?? throw new InvalidOperationException("Encrypted key without a value.");
        return XElement.Parse(Encoding.UTF8.GetString(master.Decrypt(MasterKeyXmlEncryptor.Purpose, Convert.FromBase64String(value))));
    }
}
