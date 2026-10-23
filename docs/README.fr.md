# Baseera

Application Windows expérimentale : analyse locale des fichiers, traduction de
fenêtres avec Gemini, mises à jour WinGet choisies et conversion d’images/PDF.
L’interface est disponible en arabe (RTL), anglais, français et espagnol.

Prérequis : Windows 10/11 x64. Le runtime .NET est inclus. Pour compiler avec le SDK 10 :
`./build.ps1 -Test -Publish`. Lancez `artifacts/Baseera-v0.4.9-Windows-x64/Baseera.exe`.
L’extension de navigateur est un téléchargement séparé : suivez `START-HERE.html` et chargez le dossier `extension` dans Chrome/Edge. Ne lancez pas ses fichiers source par double-clic.

Activez uniquement les outils voulus lors du premier lancement. Pour la traduction,
choisissez parmi 20 services directs, dont Gemini, OpenAI (modèles ChatGPT), Claude et Grok. Ajoutez la clé propre au service, puis chargez les modèles avec vision. OpenRouter est supprimé ; le plantage lors du changement de service est corrigé. L’accès et le prix dépendent du compte. [Services et vérification](AI-SERVICES.md). La capture est envoyée sur demande. Échap ferme la couche.
L’extension dans `extension/` traduit le texte de la page en place et restaure l’original.

L’assistant d’erreurs figure dans « Vos outils », sur l’accueil et dans la barre latérale. Sa page ouvre la fenêtre d’analyse ; lorsqu’il est activé, elle s’ouvre aussi au démarrage normal. Vous pouvez désactiver cet outil dans les paramètres. Collez
un message ou une image, collez un fichier image copié, choisissez un fichier ou capturez une fenêtre ouverte ; l’aperçu apparaît aussitôt. Demandez ensuite une analyse avec la clé choisie. Vous pouvez fermer l’assistant sans
configurer de clé et le rouvrir avec Ctrl+Shift+E depuis Baseera. Le texte et l’image
choisis sont envoyés au service choisi seulement après votre clic directement. Vérifiez la capture avant
l’envoi : elle peut contenir des données privées. Les conseils ne sont pas une
réparation garantie et aucun réglage Windows n’est modifié automatiquement.

« Avis et remarques », juste au-dessus des paramètres, ouvre un ticket GitHub prérempli que vous vérifiez et envoyez dans le navigateur. Aucune clé API ni image n’est jointe automatiquement.

L’analyse est statique, limitée, et ne certifie pas qu’un fichier est sûr ou malveillant.
Les sources de mises à jour manuelles prennent actuellement en charge GitHub Releases
pour consultation ; leurs installateurs ne sont pas exécutés automatiquement.
Consultez [les capacités et limites](ROADMAP.md).

Depuis 0.4.0, Baseera vérifie son dépôt officiel au lancement puis toutes les deux heures. La fenêtre de mise à jour permet de télécharger, vérifier, remplacer cette copie et redémarrer, en conservant vos paramètres. Une sauvegarde permet le retour à la version précédente en cas d’échec. Vérification manuelle et option automatique dans Paramètres → Mises à jour de Baseera. Gardez le fichier dans un dossier accessible en écriture. Les versions antérieures nécessitent un téléchargement manuel de 0.4.0.

**Connexion sans clé :** choisissez OpenAI puis « Continuer avec ChatGPT ». Terminez la connexion et le consentement dans le navigateur, chargez les modèles du compte et enregistrez. Un forfait éligible et une autorisation sont nécessaires. Les autres services restent en mode clé API. [Détails et limites](CHATGPT-SIGN-IN.md).
