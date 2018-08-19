using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.MediaServices.Client;

using Microsoft.IdentityModel.Clients.ActiveDirectory;

public class Program
{
    public static IConfiguration Configuration { get; set; }
    
    // Field for service context.
    private static CloudMediaContext _context = null;
    public static int Main(string[] args = null)
    {
        // Test if there are arguments
        if (args.Length == 0)
        {
            System.Console.WriteLine("Usage: Voice2Text <filename>\n  filename = Path to file.");
            return 1;
        }

        // Test if the argument is a file
        string sFilename = Path.GetFullPath(args[0]); 
        if (!File.Exists(sFilename))
        {
            Console.WriteLine("File {0} does not exist.", sFilename);
            return 1;
        }
        
        // Setup File and config file
        string sDirectory = Path.GetDirectoryName(sFilename);
        string sConfigFile = sDirectory + @"\config.json";
 
        // Get Configuration from appsettings.json
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");
        Configuration = builder.Build();
        string sTenant = Configuration["AMSTenantDomain"];
        string sAPIep =  Configuration["AMSRestAPIEndpoint"];
        string sClientId =  Configuration["AMSAClientId"];
        string sClientSecret =  Configuration["AMSClientSecret"];

        // Disable console logging in Threds
        LoggerCallbackHandler.UseDefaultLogging = false;

        AzureAdTokenCredentials tokenCredentials = 
                new AzureAdTokenCredentials(sTenant,
                    new AzureAdClientSymmetricKey(sClientId,sClientSecret),
                    AzureEnvironments.AzureCloudEnvironment);

        // Create Cloud Media context
        var tokenProvider = new AzureAdTokenProvider(tokenCredentials);
        _context = new CloudMediaContext(new Uri(sAPIep), tokenProvider);

        // Run indexing job.
        var outputAsset = RunIndexingJob(sFilename,sConfigFile);
        
        if (outputAsset != null)
        {
            Console.WriteLine("Downloading output...");
            DownloadAssetToLocal(outputAsset, sDirectory);
            // Cleaning up Output Asset
            outputAsset.Delete();
            // Convert the VTT file to text file
            ProcessVTTfile(sFilename);
            Console.WriteLine("Done.");
        }

        return 0;
    }

    static IAsset RunIndexingJob(string inputMediaFilePath, string configurationFile)
    {
        Console.WriteLine("Preparing to Upload '{0}'", inputMediaFilePath);
        // Create an asset and upload the input media file to storage.
        IAsset asset = UploadFile(inputMediaFilePath, AssetCreationOptions.None);
      
        // Declare a new job.
        IJob job = _context.Jobs.Create("Voice2Text Job");

        // Get a reference to Azure Media Indexer 2 Preview.
        string MediaProcessorName = "Azure Media Indexer 2 Preview";

        var processor = GetLatestMediaProcessorByName(MediaProcessorName);
        
        Console.WriteLine("Reading Configuration File {0}", configurationFile);
        // Read configuration from the specified file.
        string configuration = File.ReadAllText(configurationFile);

        // Create a task with the encoding details, using a string preset.
        ITask task = job.Tasks.AddNew("Voice2Text Task",
                processor,
                configuration,
                TaskOptions.None);

        // Specify the input asset to be indexed.
        task.InputAssets.Add(asset);
     
        // Add an output asset to contain the results of the job.
        task.OutputAssets.AddNew("Voice2Text Output", AssetCreationOptions.None);

        // Use the following event handler to check job progress.  
        job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);
       
        Console.WriteLine("Submitting Speach Recognition Job.");
        // Launch the job.
        job.Submit();
        

        // Check job execution and wait for job to finish.
        Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);
    
        // Show Job Progress until Job completed, Escape key cancels
        while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
        {
            Console.Write("\rProcessing - {0:0}% {1}", job.GetOverallProgress(), progressJobTask.Status);
            if ( progressJobTask.IsCompleted || progressJobTask.IsCompletedSuccessfully || progressJobTask.IsCanceled || progressJobTask.IsFaulted) break;
        }

        // If job state is Error, the event handling
        // method for job progress should log errors.  Here we check
        // for error state and exit if needed.
        if (job.State == JobState.Error)
        {
            ErrorDetail error = job.Tasks.First().ErrorDetails.First();
            Console.WriteLine(string.Format("Error: {0}. {1}",
                                            error.Code,
                                            error.Message));
            return null;
        }
        Console.WriteLine("\nCleaning Input Asset: " + asset.Name);
        asset.Delete();

        return job.OutputMediaAssets[0];
    }

    void TaskProgressChangedEvent(object sender, EventArgs e)
    {  
        // Do something useful here.
        Console.WriteLine(e);
    }  

    static public IAsset UploadFile(string fileName, AssetCreationOptions options)
    {
        IAsset inputAsset = _context.Assets.CreateFromFile(
            fileName,
            options,
            (af, p) =>
            {
                Console.Write("\rUploading '{0}' - Progress: {1:0.##}%  ", af.Name, p.Progress);
            });

        Console.WriteLine("Done.");

        return inputAsset;
    }

    // Download the output asset of the specified job to a local folder.
    static IAsset DownloadAssetToLocal(IAsset outputAsset, string outputFolder)
    {

        // Create a SAS locator to download the asset
        IAccessPolicy accessPolicy = _context.AccessPolicies.Create("File Download Policy", TimeSpan.FromDays(30), AccessPermissions.Read);
        ILocator locator = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset, accessPolicy);

        BlobTransferClient blobTransfer = new BlobTransferClient
        {
            NumberOfConcurrentTransfers = 20,
            ParallelTransferThreadCount = 20
        };

        var downloadTasks = new List<Task>();
        foreach (IAssetFile outputFile in outputAsset.AssetFiles)
        {
            // Use the following event handler to check download progress.
            outputFile.DownloadProgressChanged += DownloadProgress;

            string localDownloadPath = Path.Combine(outputFolder, outputFile.Name);

            Console.WriteLine("File download path:  " + localDownloadPath);

            downloadTasks.Add(outputFile.DownloadAsync(Path.GetFullPath(localDownloadPath), blobTransfer, locator, CancellationToken.None));

            outputFile.DownloadProgressChanged -= DownloadProgress;
        }

        Task.WaitAll(downloadTasks.ToArray());

        return outputAsset;
    }

    static void DownloadProgress(object sender, DownloadProgressChangedEventArgs e)
    {
        Console.WriteLine(string.Format("{0} % download progress. ", e.Progress));
    }
    static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
    {
        var processor = _context.MediaProcessors
            .Where(p => p.Name == mediaProcessorName)
            .ToList()
            .OrderBy(p => new Version(p.Version))
            .LastOrDefault();

        if (processor == null)
            throw new ArgumentException(string.Format("Unknown media processor", mediaProcessorName));

        return processor;
    }

    static private void StateChanged(object sender, JobStateChangedEventArgs e)
    {
        switch (e.CurrentState)
        {
            case JobState.Finished:
                Console.WriteLine("\nJob is Done.                  ");
                break;
            case JobState.Canceling:
                Console.WriteLine("\rJob is Canceling...           ");
                break;
            case JobState.Queued:
                Console.WriteLine("\rJob is Queued...              ");
                break;
            case JobState.Scheduled:
                Console.WriteLine("\rJob is Scheduled.             ");
                break;
            case JobState.Processing:
                Console.WriteLine("\rProcessing Job. Please wait...");
                break;
            case JobState.Canceled:
                Console.WriteLine("\nJob is CANCELED.\n");
                break;            
            case JobState.Error:
                // Cast sender as a job.
                IJob job = (IJob)sender;
                // Display or log error details as needed.
                // LogJobStop(job.Id);
                break;
            default:
                break;
        }
    }

    static void ProcessVTTfile(string sInputFile)
    {
        string sDirectory = Path.GetDirectoryName(sInputFile);
        string sFileName = sDirectory + @"\" + Path.GetFileNameWithoutExtension(sInputFile) + @"_aud_SpReco.vtt";
        // Console.WriteLine(sFileName);
        if (File.Exists(sFileName))
        {
            var lines = File.ReadAllLines(sFileName);
            var cleanLines = (from s in lines
                                where   (s.IndexOf("-->") < 0) && 
                                         (s.Length > 0) &&
                                        (!s.StartsWith("NOTE Confidence:"))
                                select s);
 
        System.IO.File.WriteAllLines(sFileName + @".txt", cleanLines);
        }
    }
}