# Baseera

Application Windows expérimentale : analyse locale des fichiers, traduction de
fenêtres avec Gemini, mises à jour WinGet choisies et conversion d’images/PDF.
L’interface est disponible en arabe (RTL), anglais, français et espagnol.

Prérequis : Windows 10/11 et .NET Desktop Runtime 10. Pour compiler avec le SDK 10 :
`./build.ps1 -Test -Publish`. Lancez `artifacts/Baseera-win-x64/Baseera.exe`.

Activez uniquement les outils voulus lors du premier lancement. La traduction
nécessite votre clé Gemini et un modèle compatible avec les images. Elle envoie
la capture de la fenêtre sélectionnée à Google sur demande. Échap ferme la couche.
L’extension dans `extension/` traduit le texte de la page en place et restaure l’original.

L’analyse est statique, limitée, et ne certifie pas qu’un fichier est sûr ou malveillant.
Les sources de mises à jour manuelles prennent actuellement en charge GitHub Releases
pour consultation ; leurs installateurs ne sont pas exécutés automatiquement.
Consultez [les capacités et limites](ROADMAP.md).
