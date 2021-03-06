﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using VersFx.Formats.Text.Epub.Entities;
using VersFx.Formats.Text.Epub.Readers;
using VersFx.Formats.Text.Epub.Schema.Navigation;
using VersFx.Formats.Text.Epub.Schema.Opf;
using VersFx.Formats.Text.Epub.Utils;
using System.Threading.Tasks;
using Android.Graphics;
using Splat;
using Android.Graphics.Drawables;

namespace VersFx.Formats.Text.Epub
{
    public static class EpubReader
    {
        public async static Task<EpubBook> OpenBook(string filePath)
        {
			
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Specified epub file not found.", filePath);
            EpubBook book = new EpubBook();
            book.FilePath = filePath;
			FileStream oStream = new FileStream (filePath, FileMode.Open);
            using (ZipArchive epubArchive = ZipFile.OpenRead(filePath))
            {
                book.Schema = SchemaReader.ReadSchema(epubArchive);
                book.Title = book.Schema.Package.Metadata.Titles.FirstOrDefault() ?? String.Empty;
                book.AuthorList = book.Schema.Package.Metadata.Creators.Select(creator => creator.Creator).ToList();
                book.Author = String.Join(", ", book.AuthorList);
                book.Content = ContentReader.ReadContentFiles(epubArchive, book);

				book.CoverImage = await LoadCoverImage (book);
                book.Chapters = LoadChapters(book, epubArchive);
            }
            return book;
        }

		private async static Task<Bitmap> LoadCoverImage(EpubBook book)
        {
            List<EpubMetadataMeta> metaItems = book.Schema.Package.Metadata.MetaItems;
            if (metaItems == null || !metaItems.Any())
                return null;
            EpubMetadataMeta coverMetaItem = metaItems.FirstOrDefault(metaItem => String.Compare(metaItem.Name, "cover", StringComparison.OrdinalIgnoreCase) == 0);
            if (coverMetaItem == null)
                return null;
            if (String.IsNullOrEmpty(coverMetaItem.Content))
                throw new Exception("Incorrect EPUB metadata: cover item content is missing");
            EpubManifestItem coverManifestItem = book.Schema.Package.Manifest.FirstOrDefault(manifestItem => String.Compare(manifestItem.Id, coverMetaItem.Content, StringComparison.OrdinalIgnoreCase) == 0);
            if (coverManifestItem == null)
                throw new Exception(String.Format("Incorrect EPUB manifest: item with ID = \"{0}\" is missing", coverMetaItem.Content));
            EpubByteContentFile coverImageContentFile;
            if (!book.Content.Images.TryGetValue(coverManifestItem.Href, out coverImageContentFile))
                throw new Exception(String.Format("Incorrect EPUB manifest: item with href = \"{0}\" is missing", coverManifestItem.Href));
			using (MemoryStream coverImageStream = new MemoryStream (coverImageContentFile.Content)) {
				//return Image.FromStream(coverImageStream);
				IBitmap profileImage = await BitmapLoader.Current.Load(coverImageStream, null /* Use original width */, null /* Use original height */);
				BitmapDrawable coverImage = (BitmapDrawable)profileImage.ToNative ();
				//Bitmap CoverImage = im.Bitmap;
				return coverImage.Bitmap;
			}
                
        }

        private static List<EpubChapter> LoadChapters(EpubBook book, ZipArchive epubArchive)
        {
            return LoadChapters(book, book.Schema.Navigation.NavMap, epubArchive);
        }

        private static List<EpubChapter> LoadChapters(EpubBook book, List<EpubNavigationPoint> navigationPoints, ZipArchive epubArchive)
        {
            List<EpubChapter> result = new List<EpubChapter>();
            foreach (EpubNavigationPoint navigationPoint in navigationPoints)
            {
                EpubChapter chapter = new EpubChapter();
                chapter.Title = navigationPoint.NavigationLabels.First().Text;
                int contentSourceAnchorCharIndex = navigationPoint.Content.Source.IndexOf('#');
                if (contentSourceAnchorCharIndex == -1)
                    chapter.ContentFileName = navigationPoint.Content.Source;
                else
                {
                    chapter.ContentFileName = navigationPoint.Content.Source.Substring(0, contentSourceAnchorCharIndex);
                    chapter.Anchor = navigationPoint.Content.Source.Substring(contentSourceAnchorCharIndex + 1);
                }
                EpubTextContentFile htmlContentFile;
                if (!book.Content.Html.TryGetValue(chapter.ContentFileName, out htmlContentFile))
                    throw new Exception(String.Format("Incorrect EPUB manifest: item with href = \"{0}\" is missing", chapter.ContentFileName));
                chapter.HtmlContent = htmlContentFile.Content;
                chapter.SubChapters = LoadChapters(book, navigationPoint.ChildNavigationPoints, epubArchive);
                result.Add(chapter);
            }
            return result;
        }
    }
}
