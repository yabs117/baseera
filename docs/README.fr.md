# Baseera

Application Windows expérimentale : analyse locale des fichiers, traduction de
fenêtres avec Gemini, mises à jour WinGet choisies et conversion d’images/PDF.
L’interface est disponible en arabe (RTL), anglais, français et espagnol.

Prérequis : Windows 10/11 x64. Le runtime .NET est inclus. Pour compiler avec le SDK 10 :
`./build.ps1 -Test -Publish`. Lancez `artifacts/Baseera-v0.4.0-Windows-x64/Baseera.exe`.
L’extension de navigateur est un téléchargement séparé : suivez `START-HERE.html` et chargez le dossier `extension` dans Chrome/Edge. Ne lancez pas ses fichiers source par double-clic.

Activez uniquement les outils voulus lors du premier lancement. La traduction
nécessite votre clé Gemini et un modèle compatible avec les images. Elle envoie
la capture de la fenêtre sélectionnée à Google sur demande. Échap ferme la couche.
L’extension dans `extension/` traduit le texte de la page en place et restaure l’original.

L’analyse est statique, limitée, et ne certifie pas qu’un fichier est sûr ou malveillant.
Les sources de mises à jour manuelles prennent actuellement en charge GitHub Releases
pour consultation ; leurs installateurs ne sont pas exécutés automatiquement.
Consultez [les capacités et limites](ROADMAP.md).

Depuis 0.4.0, Baseera vérifie son dépôt officiel au lancement puis toutes les deux heures. La fenêtre de mise à jour permet de télécharger, vérifier, remplacer cette copie et redémarrer, en conservant vos paramètres. Une sauvegarde permet le retour à la version précédente en cas d’échec. Vérification manuelle et option automatique dans Paramètres → Mises à jour de Baseera. Gardez le fichier dans un dossier accessible en écriture. Les versions antérieures nécessitent un téléchargement manuel de 0.4.0.
