# SaS Key Rotation Strategy

The Azure infrastructure today contains many services that need to connect in a [multi-tenated](https://learn.microsoft.com/azure/arcclickecture/guide/multitenant/overview) fashion, some of these services are challenging for Azure Active Directory (Azure AD) to provide a service principal (which eliminates the need for multi-tenant Key rotation) thus a Shared Access Signature (SAS) token is utilized to gain access into the Azure cloud services. SAS keys must be rotated to maintain the highest integrity of cloud data and ensuring those keys are safeguarded in transit is also crucial for the system's confidentiality.

This Proof of Concept (PoC) contains a sample solution for handling cross-tenant SaS key rotation with a symmetric key encryption strategy for a Azure Service Bus between a Provider and one or more of it is Customers. This scenario can be adapted into solutions that are using services which need a key rotation strategy in any multi-tenanted capacity. For simplicity, this solution has been boiled down to a few key components but can be adapted to fit your solution's needs. There are some limitations with the Azure Azure Key Vault when creating a symmertric key solution such as not being able to utilize the Azure Key Vault encryption and decryption functionality (reserved for asymmetric key encryption) thus encryption and decrypton functionality needed to be implemented.
The structure of this repository can read as follows: the provider kicks off a powershell script that rotates the Azure Service Bus keys then kicks off an "update keys" message into the Azure Service Bus. Then another function in the provider consumes that message and grabs the keys from the service and stores the new information into a secret message then gets encrypted by an AES 256 bit key (with a 128 bit IV) and placed into another queue. A client function then gets triggered from the new message on the Azure Service Bus, retrieves the new key information, decrypts the payload, and stores the values into the client side Azure Key Vault. A client acknowledgement message is then sent back to the provider to report back on the status.

# Rotation Design Recommendations in FAQ Format

## What is the benefit of having a second layer of encryption?

The Azuure Service Bus communication is encrypted by default.  Clients are able to access data via a SAS Token derived from the SAS keys.  The security provided via this infrastructure is solely reliant on the security of the provider.  If somehow, the provider is compromised all of the communication with the clients is also compromised.  If however only the SAS Token is compromised all may not be compromised. In the recommendation for rotation of secrets, we are having the client utilize an additional layer of encryption with a symmetric encryption algorithm.  In this situation, if the SaS tokens are compromised this mechanism can protect the transmission of the rotated secrets from being tampered with.
In this PoC we are generating a symmetric key in the provider side that will be shared with the clients to encrypt the new command queue key and response queue key updates marerials being sent to another tenant. Other key materials tha should be considered but are not present in this PoC include ACR keys. In the PoC this is just a PowerShell script the provider would create and then shares that key with the client; in the architecture the bootstap phase would be the best place to share this key with the clients. This key will be used to encrypt the payload (or parts of a payload which might be more feasible) with the new SaS key matierls for the client to decrypt and add to their environments (in the poc it is just saved to the key vault). 

## Asymmetric Encryption, Symmetric Encryption, and Certs

The [security industry best practice](https://cheatsheetseries.owasp.org/cheatsheets/Key_Management_Cheat_Sheet.html) suggests to use both symmetic and asymmetric key encryption to ensure the keys are generated and passed safely along to both the provider and the client. In our scenario, we recommend using a symmetric encryption algorithm, AES, for protection of the rotated secrets. RSA is limited in the length of the plaintext by the length the RSA Keys thus Asymmetric encryption will not suffice given the number and length of secrets being rotated.

## How is a Key or Symmetric Key created and shared?

In our PoC an encryption key is generated from a Powershell cmdlet and stored in the provider key vault.  Once the payload is received from the provider on the Service Bus, the client can decrypt the payload with the symmetric key which will be stored in the client's KV.

## Azure Key Vault features of interest

The Azure Key Vault provides a number of features that will be of interest to this design. An Azure Key Vault is a sotware implementation of a Hardware Security Module (HSM). HSMs are used to protect keys so that the key never leaves the HSM hardware in the clear.
The cryptographic features replicated in the Azure Key Vault that provide the functionality to ensure encryption keys never leave the Azure Key Vault in the clear are:

| Operation | Description |
|-|-|
| [Decrypt](https://learn.microsoft.com/rest/api/keyvault/keys/decrypt) | Decrypts a single block of encrypted data for an asymmetric key. |
| [Encrypt](https://learn.microsoft.com/rest/api/keyvault/keys/encrypt) | Encrypts an arbitrary sequence of bytes using an asymmertric encryption key that is stored in a key vault. |
| [Wrap Key](https://learn.microsoft.com/rest/api/keyvault/keys/wrap-Key) | Wraps a symmetric/asymmetric key using a specified key.|
| [Unwrap Key](https://learn.microsoft.com/rest/api/keyvault/keys/unwrap-Key) | Unwraps a symmetric/asymmetric key using the specified key that was initially used for wrapping that key. |

## PoC Architecture

![PoC Infrastructure](Cross-Tenant-SAS-Key-Rotation/Docs/key-rotation.svg)

The architecture above has helped envision and generate a plan of action when we were desinging the key rotation solution.
The flow for the key rotation goes as follows:

### Step 1 in the Diagram 

This can be thought of as the IaC/ Pre-work setup to kick off key rotation which will wse a PowerShell script to roll the SaS secrets in the service bus from primary to secondary key slot as well as the AES encryption key and initialization vector (IV)


1. First a key-specific message will flow down to update the keys. It is recommended that a 30 day cadence for key updates should occur.

1. Create a powershell script to rotate the service bus message keys and include that into a key rotation pipeline or some other automation that can update the key on a regular cadence. Example for this powershell script can be found in the /scripts folder named regeneratePrimaryKey.ps1 

### Step 2 in the Diagram

For this PoC a powershell script will do this work, in the client's environment something like a durable function can be used to do the work of rotation. 2. Kick off the HTTP triggered function to initiate an “update key” message and place it on a “update key” queue.


1.  Create a Powershell script to generate a symmetic key. This key will be used for the encryption and decryption process of the newly updated SAS keys (both for service bus queues and ACR pull key) which will be added as an encrypted service bus message and delivered some queue (perhaps the command queue) for customers to poll and retreive the new key value.

1. Share the key within the customer KV/ provider’s KV (within the secrets section). This can potentially be done in a IaC script, generated on the customers behalf, or apart of onboarding a customer in a Bring Your Own Key (BYOK) service.

### Step 3 in the Diagram

1. For PoC purposes this is a no-op to just kick off the operations to simulate an "update keys" command to come into the system. Currently it is an HTTP triggered function wired up with a managed identity which will place a message in the `InitiateDeployment Queue`.

### Step 4 in the Diagram

Read the SB queues SAS token for the new secrets and store in secret message format, then encrypt the payload and add the payload onto another queue. This function is a Service bus triggered function - The initiate deployment status function consumes the message from the `InitiateDeployment Queue`

1. This is where the commmand queue and response queue keys (for now the PoC handles the command queue key, but the `Reponse Queue` keys can easily be added) will be pulled from the Service Bus (freshly rotated)

1. Bundle the keys in a secret message format

1. Encrpyt the payload with a symmetic key generated by the provider and communcated with the client, and then send the message into the command queue 

### Step 5-7 in the Diagram

This client function will consume the message, decrypt the payload, save off the keys into an Azure Key Vault and sends back an ack to the response queue.

## Provider Setup

### Azure Function - HTTP Triggered

This HTTP function is used to start off the deployment from the Provider's tenant by sending a message into the customer's Azure Service Bus deployment queue. (This HTTP triggered function was the chosen method of delivery to kick off this proof-of-concept.)

### Azure Function - Azure Service Bus Trigger

Generate an Azure Function from a Azure Service Bus queue trigger from the following documentation was followed to define the function trigger from the Azure Service Bus. https://docs.microsoft.com/azure/azure-functions/functions-identity-based-connections-tutorial-2 This documentation also showcases how to setup a managed identity used for the azure function to get triggered from the Azure Service Bus when a message has been added to the queue and we will continue to use that managed identity when putting a message into a different queue (for demo purposes, the same function was used to push the message through)
In your Azure Service Bus namespace that you just created, select Access Control (IAM). This is where you can view and configure who has access to the resource.

#### Grant the Function App Access to the Azure Service Bus Namespace using Managed Identities

*Note these directions were taken from [Identity Based Functions](https://docs.microsoft.com/azure/azure-functions/functions-identity-based-connections-tutorial-2) *
1. Click on "Access Control (IAM)"
1. Click "Add" and select "Add role assignment".
1. Search for "Azure Azure Service Bus Data Receiver", select it, and click "Next".
1. On the" Members" tab, under Assign access to, choose Managed Identity
1. Click "Select" members to open the Select managed identities panel.
Confirm that the Subscription is the one in which you created the resources earlier.
1. In the Managed identity selector, choose "Function App" from the "System-assigned managed identity" category. The label "Function App" may have a number in parentheses next to it, indicating the number of apps in the subscription with system-assigned identities.
Your app should appear in a list below the input fields. If you don't see it, you can use the Select box to filter the results with your app's name.
1. Click on your application. It should move down into the Selected members section. Click "Select".
1. Back on the Add role assignment screen, click "Review + assign". Review the configuration, and then click "Review + assign".

#### Grant the Function App Access to the Azure Key Vault using Managed Identities

1. Click on "Access Control (IAM)"
1. Click "Add" and select "Add role assignment".
1. Search for "Azure Key Vault Administrator", select it, and click "Next".
1. On the" Members" tab, under Assign access to, choose Managed Identity
1. Click "Select" members to open the Select managed identities panel.
Confirm that the Subscription is the one in which you created the resources earlier.
1. In the Managed identity selector, choose "Function App" from the "System-assigned managed identity" category. The label "Function App" may have a number in parentheses next to it, indicating the number of apps in the subscription with system-assigned identities.
Your app should appear in a list below the input fields. If you don't see it, you can use the Select box to filter the results with your app's name.
1. Click on your application. It should move down into the Selected members section. Click "Select".
1. Back on the Add role assignment screen, click "Review + assign". Review the configuration, and then click "Review + assign".

### Set up RBAC for the System Assigned Managed Identities

Scope the System Assigned Managed Identity to have "Azure Service Bus Data Owner" roles on the Azure Service Bus. This Managed Identity will be used in both writing to a queue with an HTTP triggered function, as well as reading from a queue from a timer triggered function* 
1. Navigate to the Customer Azure Service Bus namespace
1. Click on "Access Control (IAM)
1. Click on "Role Assignments" and "Add" at the top and "Add Role Assignment" 
1. Select "Azure Azure Service Bus Data Owner" and click "Next"
1. Ensure "User, group, or Service Principal" is selected and click "+Select members" in the "Select" field type in the Service Principal name and select the system assigned managed identity.
1. Select "Review + assign"

Scope the System Assigned Managed Identity to have "Azure Key Vault Administrator" roles on the Azure Service Bus. This Managed Identity will be used in both writing and reading from a Azure Key Vault with a timer triggered function* 
1. Navigate to the Customer Azure Service Bus namespace
1. Click on "Access Control (IAM)
1. Click on "Role Assignments" and "Add" at the top and "Add Role Assignment" 
1. Select "Azure Key Vault Administrator" and click "Next"
1. Ensure "User, group, or Service Principal" is selected and click "+Select members" in the "Select" field type in the Service Principal name and select the system assigned managed identity.
1. Select "Review + assign"

## Customer Setup

### Azure Function - Azure Service Bus Trigger

1. Create an Azure Service Bus triggered function to read a message off a Azure Service Bus message queue and place a message into another queue. (for demo purposes this flow as optimal to test out the functionality)

*Note! must do an az login -t CUSTOMER TENANT ID before deploying locally, this will mitigate against invalid token issuer error messages*

#### Grant the Function App Access to the Azure Key Vault using Managed Identities

1. Click on "Access Control (IAM)"
1. Click "Add" and select "Add role assignment".
1. Search for "Azure Key Vault Administrator", select it, and click "Next".
1. On the" Members" tab, under Assign access to, choose Managed Identity
1. Click "Select" members to open the Select managed identities panel.
Confirm that the Subscription is the one in which you created the resources earlier.
1. In the Managed identity selector, choose "Function App" from the "System-assigned managed identity" category. The label "Function App" may have a number in parentheses next to it, indicating the number of apps in the subscription with system-assigned identities.
Your app should appear in a list below the input fields. If you don't see it, you can use the Select box to filter the results with your app's name.
1. Click on your application. It should move down into the Selected members section. Click "Select".
1. Back on the Add role assignment screen, click "Review + assign". Review the configuration, and then click "Review + assign".

### Set up RBAC for the System Assigned Managed Identities

Scope the System Assigned Managed Identity to have "Azure Key Vault Administrator" roles on the Azure Service Bus. This Managed Identity will be used in both writing and reading from an Azure Key Vault with a timer triggered function* 
1. Navigate to the Customer Azure Service Bus namespace
1. Click on "Access Control (IAM)
1. Click on "Role Assignments" and "Add" at the top and "Add Role Assignment" 
1. Select "Azure Key Vault Administrator" and click "Next"
1. Ensure "User, group, or Service Principal" is selected and click "+Select members" in the "Select" field type in the Service Principal name and select the system assigned managed identity.
1. Select "Review + assign"

#### Connect to Azure Service Bus in your function app

*Note these directions were taken from [Identity Based Functions](https://docs.microsoft.com/azure/azure-functions/functions-identity-based-connections-tutorial-2) *
1. In the portal, search for the your function app , or browse to it in the Function App page.
1. In your function app, select "Configuration" under Settings.
1. In Application settings, select "+ New" application setting to create the new setting in the following table. "ServiceBusConnection__fullyQualifiedNamespace	<SERVICE_BUS_NAMESPACE>.servicebus.windows.net" 
1. After you create the two settings, select "Save > Confirm".

## Local Settings
Each subdirectory contains a stubbed version of the local.settings.json files which can be modified to run the Azure functions locally. To configure settings in Azure, update the Application Settings.
  
*Note!  In order to mitigate against invalid token issuer messages, you must use az login -t CUSTOMER_TENANT_ID before deploying locally.*
