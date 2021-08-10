using SmartHealthCard.QRCode;
using SmartHealthCard.Token;
using SmartHealthCard.Token.Certificates;
using SmartHealthCard.Token.Exceptions;
using SmartHealthCard.Token.Model.Shc;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace SMARTHealthCardImmunizationPoC_ConsoleApp
{
    /// <summary>
    /// FHIR Client Library Documentation
    ///     https://docs.fire.ly/projects/Firely-NET-SDK/
    /// 
    /// Sample FHIR Bundles for SMART Health Card
    ///     https://spec.smarthealth.cards/examples/
    /// 
    /// FHIR Immunization Resource
    ///     https://www.hl7.org/fhir/immunization.html
    ///     
    /// Library for generating SMART Health Cards
    ///     https://github.com/angusmillar/SmartHealthCard
    ///     https://www.nuget.org/packages/SmartHealthCard.QRCode/
    ///     
    /// Nice article about decoding
    ///     https://www.eff.org/deeplinks/2021/06/decoding-californias-new-digital-vaccine-records-and-potential-dangers
    ///     
    /// Creating Elliptical Curve Keys using OpenSSL
    ///     https://www.scottbrady91.com/OpenSSL/Creating-Elliptical-Curve-Keys-using-OpenSSL
    ///     
    ///     REM Run the following command to get past the openssl error "unable to write 'random state'"
    ///     set RANDFILE=.rnd
    ///     
    ///     REM generate a private key for a curve
    ///     openssl ecparam -name prime256v1 -genkey -noout -out imm-poc-private-key.pem
    ///     
    ///     REM generate corresponding public key
    ///     openssl ec -in imm-poc-private-key.pem -pubout -out imm-poc-public-key.pem
    ///     
    ///     REM optional: create a self-signed certificate - give it 2 years
    ///     openssl req -new -x509 -key imm-poc-private-key.pem -out imm-poc-self-signed-cert.pem -days 720
    ///     
    ///     REM optional: convert pem to pfx
    ///     openssl pkcs12 -export -inkey imm-poc-private-key.pem -in imm-poc-self-signed-cert.pem -out imm-poc-self-signed-cert.pfx
    /// 
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            //Run the Encoder demo
            EncoderDemoRunner().Wait();
        }

        private static Patient GetPatientResource()
        {
            return new Patient()
            {
                BirthDate = "1999-01-01",
                Name = new List<HumanName>()
                {
                    new HumanName().WithGiven("Bart").WithGiven("E.").AndFamily("Simpson")
                }
            };
        }

        private static Immunization GetImmunizationResource(string referencePrefix, int index, string cvx, FhirDateTime administrationDate)
        {
            var codeSystemCvx = "http://hl7.org/fhir/sid/cvx";

            return new Immunization()
            {
                // The fun "DataType"
                Occurrence = (DataType)administrationDate, 
                Patient = new ResourceReference($"{referencePrefix}:0"),
                Status = Immunization.ImmunizationStatusCodes.Completed,
                VaccineCode = new CodeableConcept(codeSystemCvx, cvx) // description is optional and we want to keep size down
            };
        }

        static async Task<string> GetTestFhirBundle()
        {
            var referencePrefix = "resource";
 

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Collection
            };

            var patientSubjectReference = new ResourceReference($"{referencePrefix}:0");

            bundle.Entry.Add(
                    new Bundle.EntryComponent()
                    {
                        FullUrl = $"{referencePrefix}:0",
                        Resource = GetPatientResource()
                    }
                );

            bundle.Entry.Add(
                    new Bundle.EntryComponent()
                    {
                        FullUrl = $"{referencePrefix}:1",
                        Resource = GetImmunizationResource(referencePrefix, 1, "207", new FhirDateTime(2021, 7, 1))
                    }
                );

            bundle.Entry.Add(
                    new Bundle.EntryComponent()
                    {
                        FullUrl = $"{referencePrefix}:2",
                        Resource = GetImmunizationResource(referencePrefix, 2, "207", new FhirDateTime(2021, 7, 29))
                    }
                );

            var serializer = new FhirJsonSerializer(new SerializerSettings()
            {
                Pretty = false,
                AppendNewLine = false
            });

            return await serializer.SerializeToStringAsync(bundle);
        }

        private static X509Certificate2 GetCertificate(string thumpprint, StoreName storeName, StoreLocation storeLocation)
        {
            var store = new X509Store(storeName, storeLocation);

            try
            {
                store.Open(OpenFlags.ReadOnly);
                var certificateCollection = store.Certificates.Find(X509FindType.FindByThumbprint, thumpprint, false);

                if (certificateCollection.Count == 0)
                {
                    throw new Exception("Certificate is not installed");
                }
                return certificateCollection[0];
            }
            finally
            {
                store.Close();
            }

        }


        static async System.Threading.Tasks.Task EncoderDemoRunner()
        {

            //Get the Certificate containing a private Elliptic Curve key using the P-256 curve
            //from the Windows Certificate Store by Thumb-print
            string CertificateThumbprint = "4cdfd4d8b070a894bc701a49cab2ba18f724c2c7";

            var Certificate = GetCertificate(CertificateThumbprint, StoreName.My, StoreLocation.CurrentUser);

            //Set the Version of FHIR in use
            string FhirVersion = "4.0.1";

            //This library does not validate that the FHIR Bundle provided is valid FHIR, it only parses it as valid JSON.      
            //I strongly suggest you use the FIRELY .NET SDK as found here: https://docs.fire.ly/projects/Firely-NET-SDK/index.html       
            //See the FHIR SMART Health Card FHIR profile site here: http://build.fhir.org/ig/dvci/vaccine-credential-ig/branches/main/index.html   

            //Set a FHIR Bundle as a JSON string. 
            //string FhirBundleJson = "[A Smart Health Card FHIR Bundle in JSON format]";
            var FhirBundleJson = await GetTestFhirBundle();

            //Set the base of the URL where any validator will retrieve the public keys from (e.g : [Issuer]/.well-known/jwks.json) 
            Uri Issuer = new Uri("https://acmecare.com/shc");

            //Set when the Smart Health Card becomes valid, (e.g the from date).
            DateTimeOffset IssuanceDateTimeOffset = DateTimeOffset.Now.AddMinutes(-1);

            //Set the appropriate VerifiableCredentialsType enum list, for more info see: see: https://smarthealth.cards/vocabulary/
            List<VerifiableCredentialType> VerifiableCredentialTypeList = new List<VerifiableCredentialType>()
              {
                VerifiableCredentialType.HealthCard,
                VerifiableCredentialType.Covid19,
                VerifiableCredentialType.Immunization
              };

            //Instantiate and populate the Smart Health Card Model with the properties we just setup
            SmartHealthCardModel SmartHealthCard = new SmartHealthCardModel(Issuer, IssuanceDateTimeOffset,
                new VerifiableCredential(VerifiableCredentialTypeList,
                  new CredentialSubject(FhirVersion, FhirBundleJson)));

            //Instantiate the Smart Health Card Encoder
            SmartHealthCardEncoder SmartHealthCardEncoder = new SmartHealthCardEncoder();

            string SmartHealthCardJwsToken = string.Empty;
            try
            {
                //Get the Smart Health Card JWS Token 
                SmartHealthCardJwsToken = await SmartHealthCardEncoder.GetTokenAsync(Certificate, SmartHealthCard);
            }
            catch (SmartHealthCardEncoderException EncoderException)
            {
                Console.WriteLine("The SMART Health Card Encoder has found an error, please see message below:");
                Console.WriteLine(EncoderException.Message);
            }
            catch (Exception Exception)
            {
                Console.WriteLine("Oops, there is an unexpected development exception");
                Console.WriteLine(Exception.Message);
            }

            //Instantiate the Smart Health Card QR Code Factory
            SmartHealthCardQRCodeEncoder SmartHealthCardQRCodeEncoder = new SmartHealthCardQRCodeEncoder();


            //Get list of SMART Health Card QR Codes images
            //Note: If the SMART Health Card JWS payload is large then it will be split up into multiple QR Code images.
            //SMART Health Card QR Code scanners can scan each image in any order to obtain the whole SMART Health Card  
            List<Bitmap> QRCodeImageList = SmartHealthCardQRCodeEncoder.GetQRCodeList(SmartHealthCardJwsToken);

            //Write to file the SMART Health Card QR Codes images      
            for (int i = 0; i < QRCodeImageList.Count; i++)
            {
                QRCodeImageList[i].Save(@$"C:\Temp\SMARTHealthCard\QRCode-{i}.png", System.Drawing.Imaging.ImageFormat.Png);
            }

            return;
        }
    }
}
