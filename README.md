# samparse
parsing the sam


# Generating classes from the xsd files included
Run the xscgen-parse.ps1 script.
Afterwards, there is an issue with a circular reference. To fix: 
- remove the whole empty partial class StandardResponseType 
- remove the nonsensical line [System.Xml.Serialization.XmlIncludeAttribute(typeof(Be.Fgov.Ehealth.Samws.V2.Core.StandardResponseType))] above the filled StandardResponseType definition

this is TODO to get fixed directly in the powershell script