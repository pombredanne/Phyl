﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Semantics;
using Microsoft.CodeAnalysis.Diagnostics.Telemetry;
using Roslyn.Utilities;

using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.CommandLine;
using Pchp.CodeAnalysis.Errors;

using Serilog;
using Newtonsoft.Json;
namespace Phyl.CodeAnalysis
{
    internal class PhylCompiler : PhpCompiler, ILogged
    {
        #region Constructors
        public PhylCompiler(string baseDirectory, string[] files)
            :base(
                 PhpCommandLineParser.Default,
                 Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ResponseFileName),
                 CreateCompilerArgs(files),
                 AppDomain.CurrentDomain.BaseDirectory,
                 baseDirectory,
                 RuntimeEnvironment.GetRuntimeDirectory(),
                 ReferenceDirectories,
                 new SimpleAnalyzerAssemblyLoader())
        {
            OuputWriter = new StringWriter(compilerOutput);
            ErrorLogger = new ErrorLogger(ErrorStream, "Phyl", Assembly.GetExecutingAssembly().GetName().Version.ToString(), Assembly.GetExecutingAssembly().GetName().Version);
            TouchedFileLogger = new TouchedFileLogger();
        }

        #endregion

        #region Overriden methods
        public override Compilation CreateCompilation(TextWriter output, TouchedFileLogger touchedFilesLogger, ErrorLogger errorLogger)
        {
            if (Arguments.SourceFiles.Count() == 0)
            {
                L.Error("No PHP source files match specification in directory {dir}.", Arguments.BaseDirectory);
                return null;
            }
            /*
            else
            {
                IEnumerable<CommandLineSourceFile> missing_files = Arguments.SourceFiles.Where(sf => !File.Exists(sf.Path));
                if (missing_files.Count() != 0)
                {
                    L.Error("No PHP source files with path {path} exist.", missing_files.Select(sf => sf.Path));
                    return null;
                }
            }
            */
            L.Info("Parsing {files} files in {base}...", Arguments.SourceFiles.Count(), Arguments.BaseDirectory);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            try
            {
                this.PhpCompilation = base.CreateCompilation(output, touchedFilesLogger, errorLogger) as PhpCompilation;
            }
            catch (Exception e)
            {
                L.Error(e, "An exception was thrown during parsing.");
                return null;
            }
            finally
            {
                sw.Stop();
                ErrorStream.Flush();
                ErrorStream.Position = 0;
                StreamReader sr = new StreamReader(ErrorStream);  
                Errors = sr.ReadToEnd();
            }
            if (this.PhpCompilation == null)
            {
                if (Output.Trim().StartsWith("error PHP2016"))
                {
                    L.Error("No PHP source files match specification in directory {dir}.", Arguments.BaseDirectory);
                    return null;
                }
                else
                {
                    L.Error("Could not parse files. Errors: {o}", Output.Trim());
                    return null;
                }
            }
            else
            {
                L.Success("Parsed {0} files in {ms} ms.", Arguments.SourceFiles.Count(), sw.ElapsedMilliseconds);
                return PhpCompilation;
            }
        }
        #endregion

        #region Properties
        public PhpCompilation PhpCompilation { get; protected set; }
        public StringWriter OuputWriter { get; }
        public string Output
        {
            get
            {
                return this.compilerOutput.ToString();
            }
        }
        public MemoryStream ErrorStream { get; protected set; } = new MemoryStream();
        public TouchedFileLogger TouchedFileLogger { get; protected set; }
        public ErrorLogger ErrorLogger { get; protected set; } 
        public string Errors { get; protected set; }
        public CompilerErrors CompilerErrors { get; protected set; }
        protected PhylLogger<PhylCompiler> L = new PhylLogger<PhylCompiler>();
        static string ReferenceDirectories
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%windir%\Microsoft.NET\assembly\GAC_MSIL");

            }
        }
        #endregion

        #region Methods
        static string[] CreateCompilerArgs(string[] args)
        {
            // implicit references
            List<Assembly> assemblies = new List<Assembly>()
            {
                typeof(object).Assembly,            // mscorlib (or System.Runtime)
                typeof(HashSet<>).Assembly,         // System.Core
                typeof(Pchp.Core.Context).Assembly,      // Peachpie.Runtime
                typeof(Pchp.Library.Strings).Assembly,   // Peachpie.Library
            };
            IEnumerable<string> refs = assemblies.Distinct().Select(ass => "/r:" + ass.Location);

            Debug.Assert(refs.Any(r => r.Contains("System.Core")));
            Debug.Assert(refs.Any(r => r.Contains("Peachpie.Runtime")));
            Debug.Assert(refs.Any(r => r.Contains("Peachpie.Library")));
            List<string> compiler_options = new List<string>()
            {
                "/target:library",
            };
            return compiler_options.Concat(refs).Concat(args).ToArray();
        }

        public CompilerErrors ParseCompilerErrors(string s)
        {
            try
            {
                return JsonConvert.DeserializeObject<CompilerErrors>(s);
            }
            catch (JsonSerializationException)
            {
                //L.Info("Could not deserialize compiler errors. Error: {0}.", jse.Message);
                return null;
            }
            catch (Exception e)
            {
                L.Error(e, "Exception thrown attempting to deserialize compiler errors.");
                return null;
            }
        }

        #endregion

        #region Fields
        StringBuilder compilerOutput = new StringBuilder(100);
        #endregion
    }

    class SimpleAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
            throw new NotImplementedException();
        }

        public Assembly LoadFromPath(string fullPath)
        {
            throw new NotImplementedException();
        }
    }
}
