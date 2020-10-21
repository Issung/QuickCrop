using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.IO;
using System.Linq;

namespace QuickCrop
{
    class Utilities
    {
        public enum FileType { Image, Video, Other };
        public static readonly string[] ImageExtensions = new string[] { "bpm", "gif", "jpeg", "jpg", "png", "tiff" };
        public static readonly string[] VideoExtensions = new string[] { "avi", "flv", "mkv", "mp4", "webm" };

        public FileType DetermineFileType(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return FileType.Other;

            string extension = Path.GetExtension(filePath).Substring(1).ToLower();

            if (ImageExtensions.Contains(extension))
                return FileType.Image;
            else if (VideoExtensions.Contains(extension))
                return FileType.Video;
            else
                return FileType.Other;
        }

        public static string OpenFolder()
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    return dialog.FileName;
                }
            }

            return null;
        }

        public static string OpenFile()
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.Filters.Add(new CommonFileDialogFilter("Video Files", string.Join(';', VideoExtensions.Select(t => $"*.{t}"))));

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    return dialog.FileName;
                }
            }

            return null;
        }

        public static string SaveFileDialog(string filters = "")
        {
            var dialog = new SaveFileDialog();

            dialog.Filter = filters;
            dialog.AddExtension = true;

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return dialog.FileName;
            }

            return null;
        }
    }
}
