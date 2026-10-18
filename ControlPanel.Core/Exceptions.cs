namespace ControlPanel.Core;

/*
("Cannot control task with your current identity permissions level. " +
 "You likely need to run this application 'as administrator' " +
 "even if you are using an administrator account."); */
public sealed class AccessRightsException : ApplicationException;
