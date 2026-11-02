### Steps To Reproduce

1. Go to https://vault.bitwarden.com/#/vault
2. Select at least 2 items to move, one of them with an Attachment
3. Move all of them to a Free organization
4. The item with an attachment gets moved to a Free organization:

![image](https://user-images.githubusercontent.com/5418577/155098218-a999cf27-d219-4135-958a-58b2abfcf30a.png)

### Expected Result

The item with the attachment should remain in your personal vault and throw the following error:

![image](https://user-images.githubusercontent.com/5418577/155097661-03716a2d-f0b9-4a0b-8756-d510f70636c7.png)

### Actual Result

The item with an attachment gets moved to a Free organization, which renders the attachment inaccessible.

### Operating System

Linux

### Web Browser

Chrome

### Build Version

2.26.1
