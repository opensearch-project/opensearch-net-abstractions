/* SPDX-License-Identifier: Apache-2.0
*
* The OpenSearch Contributors require contributions made to
* this file be licensed under the Apache-2.0 license or a
* compatible open source license.
*
* Modifications Copyright OpenSearch Contributors. See
* GitHub history for details.
*
*  Licensed to Elasticsearch B.V. under one or more contributor
*  license agreements. See the NOTICE file distributed with
*  this work for additional information regarding copyright
*  ownership. Elasticsearch B.V. licenses this file to you under
*  the Apache License, Version 2.0 (the "License"); you may
*  not use this file except in compliance with the License.
*  You may obtain a copy of the License at
*
* 	http://www.apache.org/licenses/LICENSE-2.0
*
*  Unless required by applicable law or agreed to in writing,
*  software distributed under the License is distributed on an
*  "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
*  KIND, either express or implied.  See the License for the
*  specific language governing permissions and limitations
*  under the License.
*/

using System;
using System.IO;
using System.Linq;
using OpenSearch.OpenSearch.Managed;
using OpenSearch.OpenSearch.Managed.ConsoleWriters;
using OpenSearch.OpenSearch.Managed.FileSystem;
using OpenSearch.Stack.ArtifactsApi;
using OpenSearch.Stack.ArtifactsApi.Products;
using ProcNet.Std;

namespace OpenSearch.OpenSearch.Ephemeral.Tasks.InstallationTasks
{
	public class InstallPlugins : ClusterComposeTask
	{
		public override void Run(IEphemeralCluster<EphemeralClusterConfiguration> cluster)
		{
			if (cluster.CachingAndCachedHomeExists()) return;

			var v = cluster.ClusterConfiguration.Version;

			var fs = cluster.FileSystem;
			var requiredPlugins = cluster.ClusterConfiguration.Plugins;

			if (cluster.ClusterConfiguration.ValidatePluginsToInstall)
			{
				var invalidPlugins = requiredPlugins
					.Where(p => !p.IsValid(v))
					.Select(p => p.SubProductName).ToList();
				if (invalidPlugins.Any())
					throw new OpenSearchCleanExitException(
						$"Can not install the following plugins for version {v}: {string.Join(", ", invalidPlugins)} ");
			}

			foreach (var plugin in requiredPlugins)
			{
				var includedByDefault = plugin.IsIncludedOutOfTheBox(v);
				if (includedByDefault)
				{
					cluster.Writer?.WriteDiagnostic(
						$"{{{nameof(Run)}}} SKIP plugin [{plugin.SubProductName}] shipped OOTB as of: {{{plugin.ShippedByDefaultAsOf}}}");
					continue;
				}

				var validForCurrentVersion = plugin.IsValid(v);
				if (!validForCurrentVersion)
				{
					cluster.Writer?.WriteDiagnostic(
						$"{{{nameof(Run)}}} SKIP plugin [{plugin.SubProductName}] not valid for version: {{{v}}}");
					continue;
				}

				var alreadyInstalled = AlreadyInstalled(fs, plugin.SubProductName);
				if (alreadyInstalled)
				{
					cluster.Writer?.WriteDiagnostic(
						$"{{{nameof(Run)}}} SKIP plugin [{plugin.SubProductName}] already installed");
					continue;
				}

				cluster.Writer?.WriteDiagnostic(
					$"{{{nameof(Run)}}} attempting install [{plugin.SubProductName}] as it's not OOTB: {{{plugin.ShippedByDefaultAsOf}}} and valid for {v}: {{{plugin.IsValid(v)}}}");
				//var installParameter = v.ReleaseState == ReleaseState.Released ? plugin.Moniker : UseHttpPluginLocation(cluster.Writer, fs, plugin, v);
				var installParameter = UseHttpPluginLocation(cluster.Writer, fs, plugin, v);
				if (!Directory.Exists(fs.ConfigPath)) Directory.CreateDirectory(fs.ConfigPath);
				ExecuteBinary(
					cluster.ClusterConfiguration,
					cluster.Writer,
					fs.PluginBinary,
					$"install opensearch plugin: {plugin.SubProductName}",
					"install --batch", installParameter);

				CopyConfigDirectoryToHomeCacheConfigDirectory(cluster, plugin);
			}
		}

		private static void CopyConfigDirectoryToHomeCacheConfigDirectory(
			IEphemeralCluster<EphemeralClusterConfiguration> cluster, OpenSearchPlugin plugin)
		{
			if (!cluster.ClusterConfiguration.CacheOpenSearchHomeInstallation) return;
			var fs = cluster.FileSystem;
			var cachedOpenSearchHomeFolder = Path.Combine(fs.LocalFolder, cluster.GetCacheFolderName());
			var configTarget = Path.Combine(cachedOpenSearchHomeFolder, "config");

			var configPluginPath = Path.Combine(fs.ConfigPath, plugin.SubProductName);
			var configPluginPathCached = Path.Combine(configTarget, plugin.SubProductName);
			if (!Directory.Exists(configPluginPath) || Directory.Exists(configPluginPathCached)) return;

			Directory.CreateDirectory(configPluginPathCached);
			CopyFolder(configPluginPath, configPluginPathCached);
		}

		private static bool AlreadyInstalled(INodeFileSystem fileSystem, string folderName)
		{
			var pluginFolder = Path.Combine(fileSystem.OpenSearchHome, "plugins", folderName);
			return Directory.Exists(pluginFolder);
		}

		private static string UseHttpPluginLocation(IConsoleLineHandler writer, INodeFileSystem fileSystem,
			OpenSearchPlugin plugin, OpenSearchVersion v)
		{
			var downloadLocation = Path.Combine(fileSystem.LocalFolder, $"{plugin.SubProductName}-{v}.zip");
			DownloadPluginSnapshot(writer, downloadLocation, plugin, v);
			//transform downloadLocation to file uri and use that to install from
			return new Uri(new Uri("file://"), downloadLocation).AbsoluteUri;
		}

		private static void DownloadPluginSnapshot(IConsoleLineHandler writer, string downloadLocation,
			OpenSearchPlugin plugin, OpenSearchVersion v)
		{
			if (File.Exists(downloadLocation)) return;
			var artifact = v.Artifact(Product.OpenSearchPlugin(plugin));
			var downloadUrl = artifact.DownloadUrl;
			writer?.WriteDiagnostic(
				$"{{{nameof(DownloadPluginSnapshot)}}} downloading [{plugin.SubProductName}] from {{{downloadUrl}}}");
			try
			{
				DownloadFile(downloadUrl, downloadLocation);
				writer?.WriteDiagnostic(
					$"{{{nameof(DownloadPluginSnapshot)}}} downloaded [{plugin.SubProductName}] to {{{downloadLocation}}}");
			}
			catch (Exception)
			{
				writer?.WriteDiagnostic(
					$"{{{nameof(DownloadPluginSnapshot)}}} download failed! [{plugin.SubProductName}] from {{{downloadUrl}}}");
				throw;
			}
		}
	}
}
