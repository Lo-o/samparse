// Deserialize from a file
using Be.Fgov.Ehealth.Samws.V2.Company.Submit;
using Be.Fgov.Ehealth.Samws.V2.Export;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Serialization;

var overrides = new XmlAttributeOverrides();


var serializer = new XmlSerializer(typeof(ExportCompaniesType), overrides);

using var stream = File.OpenRead("./SAM/CPN-1775613602318.xml");
var result = (ExportCompaniesType)serializer.Deserialize(stream);



[XmlRootAttribute("SupportedIp", Namespace = "http://test.com/2010/test", IsNullable = false)]
public partial class SupportedIp
{
    //[XmlElementAttribute(Namespace = "")]
    [XmlElementAttribute(Namespace = "gabbagool")]
    public string Name
    {
        get;
        set;
    } 
}


[GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[SerializableAttribute()]
[DebuggerStepThroughAttribute()]
[DesignerCategoryAttribute("code")]
[XmlTypeAttribute(Namespace = "http://test.com/2010/test")]
[XmlRootAttribute("ObjectType", Namespace = "http://test.com/2010/test", IsNullable = false)]
public partial class ObjectType
{

    /// <remarks/>
    [XmlElementAttribute(ElementName = "", Namespace = "gabbagool")]
    public LocalStrings Name
    {
        get;
        set;
    }

    /// <remarks/>
    [XmlArrayAttribute(ElementName = "Supportedip", Namespace = "")]
    [XmlArrayItemAttribute(IsNullable = false, Namespace = "")]
    public List<SupportedIp> Supportedip
    {
        get;
        set;
    }
}

public class LocalStrings
{
}