const statusElement = document.querySelector("#configuration-status");

if (!window.microsoftTeams) {
  statusElement.textContent = "Microsoft Teams SDK could not be loaded.";
  throw new Error("Microsoft Teams SDK could not be loaded.");
}

window.microsoftTeams.app.initialize()
  .then(() => {
    window.microsoftTeams.pages.config.registerOnSaveHandler(saveEvent => {
      window.microsoftTeams.pages.config.setConfig({
        entityId: "csa-meeting-coach",
        contentUrl: `${window.location.origin}/?host=teams`,
        suggestedDisplayName: "CSA Meeting Coach",
        websiteUrl: window.location.origin
      })
        .then(() => saveEvent.notifySuccess())
        .catch(error => saveEvent.notifyFailure(error.message));
    });

    window.microsoftTeams.pages.config.setValidityState(true);
    statusElement.textContent = "Ready. Select Save to add the coach to this meeting.";
  })
  .catch(error => {
    statusElement.textContent = `Teams initialization failed: ${error.message}`;
  });
