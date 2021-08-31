using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
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

namespace SMARTHealthCardImmunizationPoC_ConsoleApp
{
    /// <summary>
    ///
    /// Library for generating SMART Health Cards and used for this example
    ///     https://github.com/angusmillar/SmartHealthCard
    ///     https://www.nuget.org/packages/SmartHealthCard.QRCode/
    ///     
    /// FHIR Client Library Documentation
    ///     https://docs.fire.ly/projects/Firely-NET-SDK/
    /// 
    /// Sample FHIR Bundles for SMART Health Card
    ///     https://spec.smarthealth.cards/examples/
    /// 
    /// FHIR Immunization Resource
    ///     https://www.hl7.org/fhir/immunization.html
    ///     
    /// Relevant article(s) 
    ///     https://www.eff.org/deeplinks/2021/06/decoding-californias-new-digital-vaccine-records-and-potential-dangers
    ///     https://vishnuravi.medium.com/how-do-verifiable-covid-19-vaccination-records-with-smart-health-cards-work-df099370b27a
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
    /// <remarks>
    ///     For testing purposes we are creating a value near identical to one of the published examples
    ///     https://spec.smarthealth.cards/examples/example-00-b-jws-payload-expanded.json
    /// </remarks>
    /// <remarks>
    ///     iis urls should be meaningful, if there is not reference to the name in the trust framework 
    ///     then the url is all you have to display to the user
    /// </remarks>
    class Program
    {
        static void Main(string[] args)
        {
            //Run the Encoder demo
            EncoderDemoRunner().Wait();
        }

        static async System.Threading.Tasks.Task EncoderDemoRunner()
        {
            //Get the Certificate containing a private Elliptic Curve key using the P-256 curve
            //from the Windows Certificate Store by Thumb-print
            string CertificateThumbprint = "4cdfd4d8b070a894bc701a49cab2ba18f724c2c7";
            X509Certificate2 Certificate = X509CertificateSupport.GetFirstMatchingCertificate(
                  CertificateThumbprint.ToUpper(),
                  X509FindType.FindByThumbprint,
                  StoreName.My,
                  StoreLocation.CurrentUser,
                  true
                  );


            //Set the Version of FHIR in use
            string FhirVersion = "4.0.1";

            var FhirBundleJson = await GetTestFhirBundle();

            //Set the base of the URL where any validator will retrieve the public keys from (e.g : [Issuer]/.well-known/jwks.json) 
            
            // Just to be safe, set to all lowercase, register like this as well.  This is safety measure in case the verifier has
            // any sort of case sensitive code
            Uri Issuer = new Uri("https://testing.envisiontechnology.com/hl7smarthealthcarddemo".ToLower()); 

            //Set when the Smart Health Card becomes valid, (e.g the from date).
            DateTimeOffset IssuanceDateTimeOffset = DateTimeOffset.Now.AddMinutes(-1);

            //Set the appropriate VerifiableCredentialsType enum list, for more info see: see: https://smarthealth.cards/vocabulary/
            List<VerifiableCredentialType> VerifiableCredentialTypeList = new List<VerifiableCredentialType>()
              {
                VerifiableCredentialType.HealthCard,
                VerifiableCredentialType.Immunization,
                VerifiableCredentialType.Covid19
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

            // Test Code
            List<string> QRCodeRawDataList = SmartHealthCardQRCodeEncoder.GetQRCodeRawDataList(SmartHealthCardJwsToken);
            SmartHealthCardQRCodeDecoder SmartHealthCardQRCodeDecoder = new SmartHealthCardQRCodeDecoder();
            string JWS = SmartHealthCardQRCodeDecoder.GetToken(QRCodeRawDataList);

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

        private static Patient GetPatientResource(string firstName, string middleName, string lastName, Date birthDate)
        {
            return new Patient()
            {
                BirthDateElement = birthDate,
                Name = new List<HumanName>()
                {
                    new HumanName().WithGiven(firstName).WithGiven(middleName).AndFamily(lastName)
                }
            };
        }

        private static Immunization GetImmunizationResource(string referencePrefix, string cvx, FhirDateTime administrationDate, string lotNumber)
        {
            var codeSystemCvx = "http://hl7.org/fhir/sid/cvx";

            return new Immunization()
            {
                // The fun "DataType"
                Occurrence = (DataType)administrationDate, 
                Patient = new ResourceReference($"{referencePrefix}:0"),
                Status = Immunization.ImmunizationStatusCodes.Completed,
                VaccineCode = new CodeableConcept(codeSystemCvx, cvx), // description is optional and we want to keep size down
                LotNumber = lotNumber,
                Performer = new List<Immunization.PerformerComponent>()
                {
                   new Immunization.PerformerComponent()
                   {
                       Actor = new ResourceReference()
                       {
                           Display = "ABC General Hospital"
                       }
                   }
                }
            };
        }

        private static async Task<string> GetTestFhirBundle()
        {
            var referencePrefix = "resource";
 
            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Collection
            };

            bundle.Entry.Add(
                    new Bundle.EntryComponent()
                    {
                        FullUrl = $"{referencePrefix}:0",
                        Resource = GetPatientResource("John", "B.", "Anyperson", new Date(1951, 1, 20))
                    }
                ); ;

            bundle.Entry.Add(
                    new Bundle.EntryComponent()
                    {
                        FullUrl = $"{referencePrefix}:1",
                        Resource = GetImmunizationResource(referencePrefix, "207", new FhirDateTime(2021, 1, 1), "0000001")
                    }
                );

            bundle.Entry.Add(
                    new Bundle.EntryComponent()
                    {
                        FullUrl = $"{referencePrefix}:2",
                        Resource = GetImmunizationResource(referencePrefix, "207", new FhirDateTime(2021, 1, 29), "0000007")
                    }
                );

            var serializer = new FhirJsonSerializer(new SerializerSettings()
            {
                Pretty = false,
                AppendNewLine = false
            });

            return await serializer.SerializeToStringAsync(bundle);
        }
    }
}
