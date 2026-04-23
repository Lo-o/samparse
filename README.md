# samparse
parsing the sam

## Projects

- **`samparse/`** — class library with XSD-generated C# types and `SamLoader`
- **`trial/`** — console app for ad-hoc data exploration
- **`DisplayParagraph/`** — Blazor Web App; navigate to `/paragraph-viewer` to browse Chapter IV paragraph verses interactively

## Getting the SAM data

Download the latest SAM database export (v6) from:
**https://www.vas.ehealth.fgov.be/websamcivics/samcivics/**

Select the latest v6 export and extract the XML files into:
```
trial/SAM/
```

This folder is gitignored and not included in the repository.

The `DisplayParagraph` Blazor app reads from the same folder by default (`SamDataPath` in `appsettings.json` resolves to `trial/SAM/` relative to the solution root).

# Generating classes from the xsd files included
Run the xscgen-parse.ps1 script.
Afterwards, there is an issue with a circular reference. To fix: 
- remove the whole empty partial class StandardResponseType 
- remove the nonsensical line [System.Xml.Serialization.XmlIncludeAttribute(typeof(Be.Fgov.Ehealth.Samws.V2.Core.StandardResponseType))] above the filled StandardResponseType definition

this is TODO to get fixed directly in the powershell script

## Issues met namespace conflicts: 
https://stackoverflow.com/questions/10532271/the-xml-element-named-name-from-namespace-references-distinct-types

bijvoorbeeld conflict tussen deze 2: 

`
[System.Xml.Serialization.XmlElementAttribute("Title", Namespace="urn:be:fgov:ehealth:samws:v2:reimbursementlaw:submit")]
public Be.Fgov.Ehealth.Samws.V2.Core.Text255Type Title { get; set; }
`

`
[System.Xml.Serialization.XmlElementAttribute("Title", Namespace="urn:be:fgov:ehealth:samws:v2:reimbursementlaw:submit")]
public Be.Fgov.Ehealth.Samws.V2.Core.TextType Title { get; set; }
`


## Aanpassingen gedaan: 
Handmatige resolutio naar logische situatie (alle Text255Type veranderd naar TextType (meer permissive)

voor deze: 
- 'Title' : [System.Xml.Serialization.XmlElementAttribute("Title", Namespace="urn:be:fgov:ehealth:samws:v2:reimbursementlaw:submit")]
- 'Type' : [System.Xml.Serialization.XmlElementAttribute("Type", Namespace="urn:be:fgov:ehealth:samws:v2:reimbursementlaw:submit")]
- 'AdditionalInformation' : [System.Xml.Serialization.XmlElementAttribute("AdditionalInformation", Namespace="urn:be:fgov:ehealth:samws:v2:actual:common")]
- 'Impact' : [System.Xml.Serialization.XmlElementAttribute("Impact", Namespace="urn:be:fgov:ehealth:samws:v2:actual:common")]


urn:be:fgov:ehealth:samws:v2:actual:common
[System.Xml.Serialization.XmlElementAttribute("Impact", Namespace="urn:be:fgov:ehealth:samws:v2:actual:common")]

