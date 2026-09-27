using System.Drawing;

namespace DocumentRepository;

internal static class UiFonts
{
	public static readonly Font Title = new Font("Microsoft YaHei UI", 16f, (FontStyle)1);

	public static readonly Font Heading = new Font("Microsoft YaHei UI", 13f, (FontStyle)1);

	public static readonly Font SubHeading = new Font("Microsoft YaHei UI", 11f, (FontStyle)1);

	public static readonly Font Body = new Font("Microsoft YaHei UI", 9.5f);

	public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9.5f, (FontStyle)1);

	public static readonly Font Caption = new Font("Microsoft YaHei UI", 9f);

	public static readonly Font Small = new Font("Microsoft YaHei UI", 8.5f);
}
