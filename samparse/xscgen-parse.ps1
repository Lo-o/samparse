$xsdFiles = Get-ChildItem -Path .\Schemas\samv2-xsd-6.0.2 -Recurse -Filter "*.xsd" | Select-Object -ExpandProperty FullName
xscgen $xsdFiles --output Generated/ --interface-