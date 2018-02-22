# voice2text
Use Azure Media Indexing Service to convert Audio or Video files to text caption files.

This console project shows how to use the Azure Media Index 2 Service in a practical way.
The tool accepts a file via command line and converts it to several caption files.
The VTT file is processed to make human readable.

## Requirements
You will need to deploy a azure media service with service principle authentication.
Create an appconfig.json file with you Azure media settings inside it like this:
```json
{
    "AMSTenantDomain": "microsoft.onmicrosoft.com",
    "AMSRestAPIEndpoint": "https://myazmediaep.restv2.westeurope.media.azure.net/api/",
    "AMSAClientId": "ed9fd949-b8df-4f49-9d38-20828f9973d1",
    "AMSClientSecret": "D6fFsNr1qaLUd925HcHF9385l9MbbhZe6mFS2znV77U="
}
```
Replace the above with your own Domain, API Endpoint, Client ID and Secret.

## References
 - [Azure Media Service Overview](https://docs.microsoft.com/en-us/azure/media-services/media-services-overview)
  - [Azure Media Indexing Service 2](https://docs.microsoft.com/en-us/azure/media-services/media-services-process-content-with-indexer2)

